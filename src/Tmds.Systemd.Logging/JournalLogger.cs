using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Tmds.Systemd.Logging
{
    class JournalLogger : ILogger
    {
        private static readonly JournalFieldName Logger = "LOGGER";
        private static readonly JournalFieldName EventId = "EVENTID";
        private static readonly JournalFieldName Exception = "EXCEPTION";
        private static readonly JournalFieldName ExceptionType = "EXCEPTION_TYPE";
        private static readonly JournalFieldName ExceptionStackTrace = "EXCEPTION_STACKTRACE";
        private static readonly JournalFieldName InnerException = "INNEREXCEPTION";
        private static readonly JournalFieldName InnerExceptionType = "INNEREXCEPTION_TYPE";
        private static readonly JournalFieldName InnerExceptionStackTrace = "INNEREXCEPTION_STACKTRACE";
        private const string OriginalFormat = "{OriginalFormat}";

        private readonly LogFlags _additionalFlags;
        private readonly string   _syslogIdentifier;

        internal JournalLogger(string name, IExternalScopeProvider scopeProvider, JournalLoggerOptions options)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Name = name;
            ScopeProvider = scopeProvider;
            if (options.DropWhenBusy)
            {
                _additionalFlags |= LogFlags.DropWhenBusy;
            }
            _syslogIdentifier = options.SyslogIdentifier;
            if (_syslogIdentifier != null)
            {
                _additionalFlags |= LogFlags.DontAppendSyslogIdentifier;
            }
        }

        internal IExternalScopeProvider ScopeProvider { get; set; }

        public string Name { get; }

        public IDisposable BeginScope<TState>(TState state) => ScopeProvider?.Push(state) ?? NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
            {
                return false;
            }

            return Journal.IsSupported;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            if (formatter == null)
            {
                throw new ArgumentNullException(nameof(formatter));
            }

            string message = formatter(state, exception);

            LogFlags flags = LogFlags.None;
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    flags = LogFlags.Debug; break;
                case LogLevel.Information:
                    flags = LogFlags.Information; break;
                case LogLevel.Warning:
                    flags = LogFlags.Warning; break;
                case LogLevel.Error:
                    flags = LogFlags.Error; break;
                case LogLevel.Critical:
                    flags = LogFlags.Critical; break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(logLevel));
            }
            flags |= _additionalFlags;

            if (!string.IsNullOrEmpty(message) || exception != null)
            {
                using (var logMessage = Journal.GetMessage())
                {
                    if (_syslogIdentifier != null)
                    {
                        logMessage.Append(JournalFieldName.SyslogIdentifier, _syslogIdentifier);
                    }
                    logMessage.Append(Logger, Name);
                    if (eventId.Id != 0 || eventId.Name != null)
                    {
                        logMessage.Append(EventId, eventId.Id);
                    }
                    if (exception != null)
                    {
                        logMessage.Append(Exception, exception.Message);
                        logMessage.Append(ExceptionType, exception.GetType().FullName);
                        logMessage.Append(ExceptionStackTrace, exception.StackTrace);
                        Exception innerException = exception.InnerException;
                        if (innerException != null)
                        {
                            logMessage.Append(InnerException, innerException.Message);
                            logMessage.Append(InnerExceptionType, innerException.GetType().FullName);
                            logMessage.Append(InnerExceptionStackTrace, innerException.StackTrace);
                        }
                    }
                    if (!string.IsNullOrEmpty(message))
                    {
                        logMessage.Append(JournalFieldName.Message, message);
                    }
                    var scopeProvider = ScopeProvider;
                    if (scopeProvider != null)
                    {
                        scopeProvider.ForEachScope((scope, msg) => AppendScope(scope, msg), logMessage);
                    }
                    if (state != null)
                    {
                        AppendState("STATE", state, logMessage);
                    }
                    Journal.Log(flags, logMessage);
                }
            }
        }

        private static void AppendScope(object scope, JournalMessage message)
            => AppendState("SCOPE", scope, message);

        private static void AppendState(string fieldName, object state, JournalMessage message)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object>> keyValuePairs)
            {
                for (int i = 0; i < keyValuePairs.Count; i++)
                {
                    var pair = keyValuePairs[i];
                    if (pair.Key == OriginalFormat)
                    {
                        continue;
                    }
                    message.Append(pair.Key, pair.Value);
                }
            }
            else
            {
                message.Append(fieldName, state);
            }
        }

        /// <summary>
        /// An empty scope without any logic
        /// </summary>
        class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();

            private NullScope()
            {
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}