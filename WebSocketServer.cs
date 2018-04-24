using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Fleck
{
	public class WebSocketServer : IWebSocketServer
	{
		Action<IWebSocketConnection> _config;

		public WebSocketServer(string location, Microsoft.Extensions.Logging.ILoggerFactory loggerFactory = null)
		{
			Logger.AssignLoggerFactory(loggerFactory);
			var uri = new Uri(location);

			this.Port = uri.Port;
			this.Location = location;
			this.LocationIP = this.ParseIPAddress(uri);
			this.Scheme = uri.Scheme;

			this.Listener = new SocketWrapper(new Socket(this.LocationIP.AddressFamily, SocketType.Stream, ProtocolType.IP));
			this.SupportedSubProtocols = new string[0];
		}

		public ISocket Listener { get; set; }

		IPAddress LocationIP { get; set; }

		string Scheme { get; set; }

		public string Location { get; private set; }

		public int Port { get; private set; }

		public X509Certificate2 Certificate { get; set; }

		public SslProtocols EnabledSslProtocols { get; set; }

		public IEnumerable<string> SupportedSubProtocols { get; set; }

		public bool RestartAfterListenError { get; set; }

		public bool IsSecure
		{
			get { return Scheme == "wss" && Certificate != null; }
		}

		public void Dispose()
		{
			this.Listener.Dispose();
		}

		IPAddress ParseIPAddress(Uri uri)
		{
			var ipStr = uri.Host;

			if (ipStr == "0.0.0.0")
				return IPAddress.Any;

			if (ipStr == "[0000:0000:0000:0000:0000:0000:0000:0000]")
				return IPAddress.IPv6Any;

			try
			{
				return IPAddress.Parse(ipStr);
			}
			catch (Exception ex)
			{
				throw new FormatException("Failed to parse the IP address part of the location. Please make sure you specify a valid IP address. Use 0.0.0.0 or [::] to listen on all interfaces.", ex);
			}
		}

		public void Start(Action<IWebSocketConnection> config)
		{
			this.Start(config, 1000);
		}

		public void Start(Action<IWebSocketConnection> config, int backlog)
		{
			this.Listener.Bind(new IPEndPoint(LocationIP, Port));
			this.Listener.Listen(backlog);
			this.Port = ((IPEndPoint)this.Listener.LocalEndPoint).Port;

			if (this.Scheme == "wss")
			{
				if (this.Certificate == null)
				{
					FleckLog.Error("Scheme cannot be 'wss' without a Certificate");
					return;
				}

				if (this.EnabledSslProtocols == SslProtocols.None)
				{
					this.EnabledSslProtocols = SslProtocols.Tls;
					FleckLog.Trace("Using default TLS 1.0 security protocol.");
				}
			}

			this.ListenForClients();
			this._config = config;
		}

		void ListenForClients()
		{
			this.Listener.Accept(this.OnClientConnected, e =>
			{
				FleckLog.Error("Listener socket is closed", e);
				if (this.RestartAfterListenError)
				{
					FleckLog.Info("Listener socket restarting");
					try
					{
						this.Listener.Dispose();
						this.Listener = new SocketWrapper(new Socket(this.LocationIP.AddressFamily, SocketType.Stream, ProtocolType.IP));
						this.Start(this._config);
						FleckLog.Info("Listener socket restarted");
					}
					catch (Exception ex)
					{
						FleckLog.Error("Listener could not be restarted", ex);
					}
				}
			});
		}

		void OnClientConnected(ISocket clientSocket)
		{
			// socket is closed
			if (clientSocket == null)
				return; 

			FleckLog.Trace($"Client connected from {clientSocket.RemoteIpAddress}:{clientSocket.RemotePort}");
			this.ListenForClients();

			WebSocketConnection connection = null;
			connection = new WebSocketConnection(
				clientSocket,
				this._config,
				bytes => RequestParser.Parse(bytes, this.Scheme),
				request => HandlerFactory.BuildHandler(
					request,
					msg => connection.OnMessage(msg),
					connection.Close,
					bin => connection.OnBinary(bin),
					bin => connection.OnPing(bin),
					bin => connection.OnPong(bin)
				),
				s => SubProtocolNegotiator.Negotiate(this.SupportedSubProtocols, s)
			);

			if (this.IsSecure)
			{
				FleckLog.Trace("Attempting to secure connection...");
				clientSocket.Authenticate(Certificate, EnabledSslProtocols, connection.StartReceiving, ex => FleckLog.Error($"Cannot secure the connection: {ex.Message}", ex));
			}
			else
				connection.StartReceiving();
		}

		public static void AssignLoggerFactory(Microsoft.Extensions.Logging.ILoggerFactory loggerFactory)
		{
			Logger.AssignLoggerFactory(loggerFactory);
		}
	}
}
