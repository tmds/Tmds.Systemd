using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions.Internal;

namespace Tmds.Systemd.Logging
{
    public class JournalLogger : ILogger
    {
        private Func<string, LogLevel, bool> _filter;

        public JournalLogger(string name, Func<string, LogLevel, bool> filter, bool includeScopes)
            : this(name, filter, includeScopes ? new LoggerExternalScopeProvider() : null)
        {
        }

        internal JournalLogger(string name, Func<string, LogLevel, bool> filter, IExternalScopeProvider scopeProvider)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Name = name;
            Filter = filter ?? ((category, logLevel) => true);
            ScopeProvider = scopeProvider;
        }

        public Func<string, LogLevel, bool> Filter
        {
            get { return _filter; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                _filter = value;
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

            return Filter(Name, logLevel);
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

            if (!string.IsNullOrEmpty(message) || exception != null)
            {
                using (var logMessage = ServiceManager.GetJournalMessage())
                {
                    logMessage.Append("LOGGER", Name);
                    if (eventId.Id != 0 || eventId.Name != null)
                    {
                        logMessage.Append("EVENTID", eventId.Id);
                    }
                    if (exception != null)
                    {
                        logMessage.Append("EXCEPTION", exception.Message);
                        logMessage.Append("EXCEPTION_TYPE", exception.GetType().FullName);
                        logMessage.Append("EXCEPTION_STACKTRACE", exception.StackTrace);
                        Exception innerException = exception.InnerException;
                        if (innerException != null)
                        {
                            logMessage.Append("INNEREXCEPTION", innerException.Message);
                            logMessage.Append("INNEREXCEPTION_TYPE", innerException.GetType().FullName);
                            logMessage.Append("INNEREXCEPTION_STACKTRACE", innerException.StackTrace);
                        }
                    }
                    if (!string.IsNullOrEmpty(message))
                    {
                        logMessage.Append("MESSAGE", message);
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
                    ServiceManager.Log(flags, logMessage);
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
                    message.Append(pair.Key, pair.Value);
                }
            }
            else
            {
                message.Append(fieldName, state);
            }
        }
    }
}