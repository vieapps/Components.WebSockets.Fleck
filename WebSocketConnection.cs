using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Fleck
{
	public class WebSocketConnection : IWebSocketConnection
	{
		public WebSocketConnection(ISocket socket, Action<IWebSocketConnection> initialize, Func<byte[], WebSocketHttpRequest> parseRequest, Func<WebSocketHttpRequest, IHandler> handlerFactory, Func<IEnumerable<string>, string> negotiateSubProtocol)
		{
			this.Socket = socket;
			this.OnOpen = () => { };
			this.OnClose = () => { };
			this.OnMessage = x => { };
			this.OnBinary = x => { };
			this.OnPing = x => this.SendPong(x);
			this.OnPong = x => { };
			this.OnError = x => { };
			this._initialize = initialize;
			this._handlerFactory = handlerFactory;
			this._parseRequest = parseRequest;
			this._negotiateSubProtocol = negotiateSubProtocol;
		}

		static int _BufferLength = 1024 * 4;

		public static int BufferLength { get { return WebSocketConnection._BufferLength; } }

		public static void SetBufferLength(int length = 4096)
		{
			if (length > 0)
				WebSocketConnection._BufferLength = length;
		}

		public ISocket Socket { get; set; }

		readonly Action<IWebSocketConnection> _initialize;
		readonly Func<WebSocketHttpRequest, IHandler> _handlerFactory;
		readonly Func<IEnumerable<string>, string> _negotiateSubProtocol;
		readonly Func<byte[], WebSocketHttpRequest> _parseRequest;

		public IHandler Handler { get; set; }

		bool _closing;
		bool _closed;

		public Action OnOpen { get; set; }

		public Action OnClose { get; set; }

		public Action<string> OnMessage { get; set; }

		public Action<byte[]> OnBinary { get; set; }

		public Action<byte[]> OnPing { get; set; }

		public Action<byte[]> OnPong { get; set; }

		public Action<Exception> OnError { get; set; }

		public IWebSocketConnectionInfo ConnectionInfo { get; set; }

		public bool IsAvailable
		{
			get { return !this._closing && !this._closed && this.Socket.Connected; }
		}

		public Task Send(string message)
		{
			return this.Send(message, this.Handler.FrameText);
		}

		public Task Send(byte[] message)
		{
			return this.Send(message, this.Handler.FrameBinary);
		}

		public Task SendPing(byte[] message)
		{
			return this.Send(message, this.Handler.FramePing);
		}

		public Task SendPong(byte[] message)
		{
			return this.Send(message, this.Handler.FramePong);
		}

		Task Send<T>(T message, Func<T, byte[]> createFrame)
		{
			if (this.Handler == null)
				throw new InvalidOperationException("Cannot send before handshake");

			if (!this.IsAvailable)
			{
				const string errorMessage = "Data sent while closing or after close. Ignoring.";
				FleckLog.Warn(errorMessage);
				return Task.FromException<T>(new ConnectionNotAvailableException(errorMessage));
			}

			var bytes = createFrame(message);
			return this.SendBytes(bytes);
		}

		Task SendBytes(byte[] buffer, Action callback = null)
		{
			return this.Socket.Send(
				buffer,
				() =>
				{
					FleckLog.Trace($"Sent {buffer.Length:#,##0} bytes");
					callback?.Invoke();
				},
				(ex) =>
				{
					if (ex is IOException)
						FleckLog.Debug("Failed to send. Disconnecting.", ex);
					else
						FleckLog.Error("Failed to send. Disconnecting.", ex);
					this.CloseSocket();
				}
			);
		}

		void CloseSocket()
		{
			this._closing = true;
			this.OnClose();
			this._closed = true;
			this.Socket.Close();
			this.Socket.Dispose();
			this._closing = false;
		}

		public void Close()
		{
			this.Close(WebSocketStatusCodes.NormalClosure);
		}

		public void Close(int code)
		{
			if (!this.IsAvailable)
				return;

			this._closing = true;
			if (this.Handler == null)
			{
				this.CloseSocket();
				return;
			}

			var bytes = this.Handler.FrameClose(code);
			if (bytes.Length == 0)
				this.CloseSocket();
			else
				this.SendBytes(bytes, CloseSocket);
		}

		public void CreateHandler(IEnumerable<byte> data)
		{
			var request = this._parseRequest(data.ToArray());
			if (request == null)
				return;

			this.Handler = this._handlerFactory(request);
			if (Handler == null)
				return;

			var subProtocol = this._negotiateSubProtocol(request.SubProtocols);
			this.ConnectionInfo = WebSocketConnectionInfo.Create(request, this.Socket.RemoteIpAddress, this.Socket.RemotePort, subProtocol);

			this._initialize(this);
			var handshake = this.Handler.CreateHandshake(subProtocol);
			this.SendBytes(handshake, this.OnOpen);
		}

		public void StartReceiving()
		{
			var data = new List<byte>(WebSocketConnection.BufferLength);
			var buffer = new byte[WebSocketConnection.BufferLength];
			this.Read(data, buffer);
		}

		void Read(List<byte> data, byte[] buffer)
		{
			if (!this.IsAvailable)
				return;

			this.Socket.Receive(
				buffer,
				(read) =>
				{
					if (read <= 0)
					{
						FleckLog.Trace("0 bytes read. Closing.");
						this.CloseSocket();
					}
					else
					{
						FleckLog.Trace($"{read:#,##0} bytes read");
						var bytes = buffer.Take(read);
						if (this.Handler != null)
							this.Handler.Receive(bytes);
						else
						{
							data.AddRange(bytes);
							this.CreateHandler(data);
						}

						if (!buffer.Length.Equals(WebSocketConnection.BufferLength))
						{
							data = new List<byte>(WebSocketConnection.BufferLength);
							buffer = new byte[WebSocketConnection.BufferLength];
						}

						this.Read(data, buffer);
					}
				},
				(ex) => this.HandleReadError(ex)
			);
		}

		void HandleReadError(Exception ex)
		{
			if (ex is AggregateException)
			{
				this.HandleReadError((ex as AggregateException).InnerException);
				return;
			}

			if (ex is ObjectDisposedException)
			{
				FleckLog.Debug("Swallowing ObjectDisposedException", ex);
				return;
			}

			this.OnError(ex);

			if (ex is HandshakeException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug("Error while reading", ex);
			}
			else if (ex is WebSocketException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug("Error while reading", ex);
				this.Close(((WebSocketException)ex).StatusCode);
			}
			else if (ex is SubProtocolNegotiationFailureException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug(ex.Message, ex);
				this.Close(WebSocketStatusCodes.ProtocolError);
			}
			else if (ex is IOException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug("Error while reading", ex);
				this.Close(WebSocketStatusCodes.AbnormalClosure);
			}
			else
			{
				FleckLog.Error("Application Error", ex);
				this.Close(WebSocketStatusCodes.InternalServerError);
			}
		}
	}
}
