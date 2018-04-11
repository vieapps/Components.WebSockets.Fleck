using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Security.Authentication;

namespace Fleck
{
	public class WebSocketServer : IWebSocketServer
	{
		string _scheme;
		IPAddress _locationIP;
		Action<IWebSocketConnection> _config;

		public WebSocketServer(string location, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory = null)
		{
			Logger.AssignLoggerFactory(loggerFactory);
			var uri = new Uri(location);
			this.Port = uri.Port;
			this.Location = location;
			this._locationIP = ParseIPAddress(uri);
			this._scheme = uri.Scheme;
			var socket = new Socket(_locationIP.AddressFamily, SocketType.Stream, ProtocolType.IP);
			this.ListenerSocket = new SocketWrapper(socket);
			this.SupportedSubProtocols = new string[0];
		}

		public ISocket ListenerSocket { get; set; }
		public string Location { get; private set; }
		public int Port { get; private set; }
		public X509Certificate2 Certificate { get; set; }
		public SslProtocols EnabledSslProtocols { get; set; }
		public IEnumerable<string> SupportedSubProtocols { get; set; }
		public bool RestartAfterListenError { get; set; }

		public bool IsSecure
		{
			get { return _scheme == "wss" && Certificate != null; }
		}

		public void Dispose()
		{
			ListenerSocket.Dispose();
		}

		private IPAddress ParseIPAddress(Uri uri)
		{
			string ipStr = uri.Host;

			if (ipStr == "0.0.0.0")
			{
				return IPAddress.Any;
			}
			else if (ipStr == "[0000:0000:0000:0000:0000:0000:0000:0000]")
			{
				return IPAddress.IPv6Any;
			}
			else
			{
				try
				{
					return IPAddress.Parse(ipStr);
				}
				catch (Exception ex)
				{
					throw new FormatException("Failed to parse the IP address part of the location. Please make sure you specify a valid IP address. Use 0.0.0.0 or [::] to listen on all interfaces.", ex);
				}
			}
		}

		public void Start(Action<IWebSocketConnection> config)
		{
			var ipLocal = new IPEndPoint(_locationIP, Port);
			ListenerSocket.Bind(ipLocal);
			ListenerSocket.Listen(1000);
			Port = ((IPEndPoint)ListenerSocket.LocalEndPoint).Port;
			if (_scheme == "wss")
			{
				if (Certificate == null)
				{
					FleckLog.Error("Scheme cannot be 'wss' without a Certificate");
					return;
				}

				if (EnabledSslProtocols == SslProtocols.None)
				{
					EnabledSslProtocols = SslProtocols.Tls;
					FleckLog.Trace("Using default TLS 1.0 security protocol.");
				}
			}
			ListenForClients();
			_config = config;
		}

		private void ListenForClients()
		{
			ListenerSocket.Accept(OnClientConnect, e =>
			{
				FleckLog.Error("Listener socket is closed", e);
				if (RestartAfterListenError)
				{
					FleckLog.Info("Listener socket restarting");
					try
					{
						ListenerSocket.Dispose();
						var socket = new Socket(_locationIP.AddressFamily, SocketType.Stream, ProtocolType.IP);
						ListenerSocket = new SocketWrapper(socket);
						Start(_config);
						FleckLog.Info("Listener socket restarted");
					}
					catch (Exception ex)
					{
						FleckLog.Error("Listener could not be restarted", ex);
					}
				}
			});
		}

		private void OnClientConnect(ISocket clientSocket)
		{
			if (clientSocket == null) return; // socket closed

			FleckLog.Trace($"Client connected from {clientSocket.RemoteIpAddress}:{clientSocket.RemotePort}");
			ListenForClients();

			WebSocketConnection connection = null;

			connection = new WebSocketConnection(
				clientSocket,
				_config,
				bytes => RequestParser.Parse(bytes, _scheme),
				r => HandlerFactory.BuildHandler(r,
					s => connection.OnMessage(s),
					connection.Close,
					b => connection.OnBinary(b),
					b => connection.OnPing(b),
					b => connection.OnPong(b)),
				s => SubProtocolNegotiator.Negotiate(SupportedSubProtocols, s));

			if (IsSecure)
			{
				FleckLog.Trace("Attempting to secure connection...");
				clientSocket.Authenticate(Certificate, EnabledSslProtocols, connection.StartReceiving, e => FleckLog.Error($"Cannot secure the connection: {e.Message}", e));
			}
			else
			{
				connection.StartReceiving();
			}
		}

		public static void AssignLoggerFactory(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
		{
			Logger.AssignLoggerFactory(loggerFactory);
		}
	}
}
