using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fleck
{
    public class FleckLog
    {
		static ILogger _Logger = null;

		internal static ILogger Logger
		{
			get
			{
				return FleckLog._Logger ?? (FleckLog._Logger = Fleck.Logger.CreateLogger<WebSocketServer>());
			}
		}

		public static void Trace(string message, Exception ex = null)
		{
			FleckLog.Logger.LogTrace(ex, message);
		}

		public static void Info(string message, Exception ex = null)
		{
			FleckLog.Logger.LogInformation(ex, message);
		}

		public static void Warn(string message, Exception ex = null)
        {
			FleckLog.Logger.LogWarning(ex, message);
		}

		public static void Debug(string message, Exception ex = null)
		{
			FleckLog.Logger.LogDebug(ex, message);
		}

		public static void Error(string message, Exception ex = null)
        {
			FleckLog.Logger.LogError(ex, message);
		}
    }

	#region Logger
	public static class Logger
	{
		static ILoggerFactory LoggerFactory;

		/// <summary>
		/// Assigns a logger factory
		/// </summary>
		/// <param name="loggerFactory"></param>
		public static void AssignLoggerFactory(ILoggerFactory loggerFactory)
		{
			if (Logger.LoggerFactory == null && loggerFactory != null)
				Logger.LoggerFactory = loggerFactory;
		}

		/// <summary>
		/// Gets a logger factory
		/// </summary>
		/// <returns></returns>
		public static ILoggerFactory GetLoggerFactory()
		{
			return Logger.LoggerFactory ?? new NullLoggerFactory();
		}

		/// <summary>
		/// Creates a logger
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		public static ILogger CreateLogger(Type type)
		{
			return Logger.GetLoggerFactory().CreateLogger(type);
		}

		/// <summary>
		/// Creates a logger
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <returns></returns>
		public static ILogger CreateLogger<T>()
		{
			return Logger.CreateLogger(typeof(T));
		}
	}
	#endregion

}
