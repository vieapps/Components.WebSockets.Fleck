using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Fleck
{
	public class WebSocketConnection : IWebSocketConnection
	{
		public WebSocketConnection(ISocket socket, Action<IWebSocketConnection> initialize, Func<byte[], WebSocketHttpRequest> parseRequest, Func<WebSocketHttpRequest, IHandler> handlerFactory, Func<IEnumerable<string>, string> negotiateSubProtocol)
		{
			Socket = socket;
			OnOpen = () => { };
			OnClose = () => { };
			OnMessage = x => { };
			OnBinary = x => { };
			OnPing = x => SendPong(x);
			OnPong = x => { };
			OnError = x => { };
			_initialize = initialize;
			_handlerFactory = handlerFactory;
			_parseRequest = parseRequest;
			_negotiateSubProtocol = negotiateSubProtocol;
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
			get { return !_closing && !_closed && Socket.Connected; }
		}

		public Task Send(string message)
		{
			return Send(message, Handler.FrameText);
		}

		public Task Send(byte[] message)
		{
			return Send(message, Handler.FrameBinary);
		}

		public Task SendPing(byte[] message)
		{
			return Send(message, Handler.FramePing);
		}

		public Task SendPong(byte[] message)
		{
			return Send(message, Handler.FramePong);
		}

		Task Send<T>(T message, Func<T, byte[]> createFrame)
		{
			if (Handler == null)
				throw new InvalidOperationException("Cannot send before handshake");

			if (!IsAvailable)
			{
				const string errorMessage = "Data sent while closing or after close. Ignoring.";
				FleckLog.Warn(errorMessage);
				return Task.FromException<T>(new ConnectionNotAvailableException(errorMessage));
			}

			var bytes = createFrame(message);
			return SendBytes(bytes);
		}

		public void StartReceiving()
		{
			var data = new List<byte>(WebSocketConnection.BufferLength);
			var buffer = new byte[WebSocketConnection.BufferLength];
			Read(data, buffer);
		}

		public void Close()
		{
			Close(WebSocketStatusCodes.NormalClosure);
		}

		public void Close(int code)
		{
			if (!IsAvailable)
				return;

			_closing = true;

			if (Handler == null)
			{
				CloseSocket();
				return;
			}

			var bytes = Handler.FrameClose(code);
			if (bytes.Length == 0)
				CloseSocket();
			else
				SendBytes(bytes, CloseSocket);
		}

		public void CreateHandler(IEnumerable<byte> data)
		{
			var request = _parseRequest(data.ToArray());
			if (request == null)
				return;
			Handler = _handlerFactory(request);
			if (Handler == null)
				return;
			var subProtocol = _negotiateSubProtocol(request.SubProtocols);
			ConnectionInfo = WebSocketConnectionInfo.Create(request, Socket.RemoteIpAddress, Socket.RemotePort, subProtocol);

			_initialize(this);

			var handshake = Handler.CreateHandshake(subProtocol);
			SendBytes(handshake, OnOpen);
		}

		void Read(List<byte> data, byte[] buffer)
		{
			if (!IsAvailable)
				return;

			Socket.Receive(buffer, r =>
			{
				if (r <= 0)
				{
					FleckLog.Trace("0 bytes read. Closing.");
					CloseSocket();
					return;
				}
				FleckLog.Trace(r + " bytes read");
				var readBytes = buffer.Take(r);
				if (Handler != null)
				{
					Handler.Receive(readBytes);
				}
				else
				{
					data.AddRange(readBytes);
					CreateHandler(data);
				}

				if (!buffer.Length.Equals(WebSocketConnection.BufferLength))
				{
					data = new List<byte>(WebSocketConnection.BufferLength);
					buffer = new byte[WebSocketConnection.BufferLength];
				}
				Read(data, buffer);
			},
			HandleReadError);
		}

		void HandleReadError(Exception e)
		{
			if (e is AggregateException)
			{
				HandleReadError((e as AggregateException).InnerException);
				return;
			}

			if (e is ObjectDisposedException)
			{
				FleckLog.Debug("Swallowing ObjectDisposedException", e);
				return;
			}

			OnError(e);

			if (e is HandshakeException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug("Error while reading", e);
			}
			else if (e is WebSocketException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug("Error while reading", e);
				Close(((WebSocketException)e).StatusCode);
			}
			else if (e is SubProtocolNegotiationFailureException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug(e.Message, e);
				Close(WebSocketStatusCodes.ProtocolError);
			}
			else if (e is IOException)
			{
				if (FleckLog.Logger.IsEnabled(LogLevel.Debug))
					FleckLog.Debug("Error while reading", e);
				Close(WebSocketStatusCodes.AbnormalClosure);
			}
			else
			{
				FleckLog.Error("Application Error", e);
				Close(WebSocketStatusCodes.InternalServerError);
			}
		}

		Task SendBytes(byte[] bytes, Action callback = null)
		{
			return Socket.Send(bytes, () =>
			{
				FleckLog.Trace("Sent " + bytes.Length + " bytes");
				callback?.Invoke();
			},
			e =>
			{
				if (e is IOException)
					FleckLog.Error("Failed to send. Disconnecting.", e);
				else
					FleckLog.Error("Failed to send. Disconnecting.", e);
				CloseSocket();
			});
		}

		void CloseSocket()
		{
			_closing = true;
			OnClose();
			_closed = true;
			Socket.Close();
			Socket.Dispose();
			_closing = false;
		}

	}
}
