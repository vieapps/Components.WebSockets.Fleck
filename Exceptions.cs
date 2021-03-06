﻿using System;

namespace Fleck
{
    public class ConnectionNotAvailableException : Exception
    {
        public ConnectionNotAvailableException() : base() { }

        public ConnectionNotAvailableException(string message) : base(message) { }

        public ConnectionNotAvailableException(string message, Exception innerException) : base(message, innerException) { }
    }

	public class HandshakeException : Exception
	{
		public HandshakeException() : base() { }

		public HandshakeException(string message) : base(message) { }

		public HandshakeException(string message, Exception innerException) : base(message, innerException) { }
	}

	public class SubProtocolNegotiationFailureException : Exception
	{
		public SubProtocolNegotiationFailureException() : base() { }

		public SubProtocolNegotiationFailureException(string message) : base(message) { }

		public SubProtocolNegotiationFailureException(string message, Exception innerException) : base(message, innerException) { }
	}

	public class WebSocketException : Exception
	{
		public WebSocketException(ushort statusCode) : base()
		{
			StatusCode = statusCode;
		}

		public WebSocketException(ushort statusCode, string message) : base(message)
		{
			StatusCode = statusCode;
		}

		public WebSocketException(ushort statusCode, string message, Exception innerException) : base(message, innerException)
		{
			StatusCode = statusCode;
		}

		public ushort StatusCode { get; private set; }
	}
}