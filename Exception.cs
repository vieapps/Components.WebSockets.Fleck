using System;

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
}
