using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tmds.Systemd.Logging
{
    [ProviderAlias("SystemdJournal")]
    public class JournalLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentDictionary<string, JournalLogger> _loggers = new ConcurrentDictionary<string, JournalLogger>();
        private static readonly Func<string, LogLevel, bool> falseFilter = (cat, level) => false;
        private static readonly Func<string, LogLevel, bool> trueFilter = (cat, level) => true;

        private readonly Func<string, LogLevel, bool> _filter;
        private IExternalScopeProvider _scopeProvider;
        private bool _includeScopes;

        public JournalLoggerProvider()
        {
            _filter = trueFilter;
        }

        public JournalLoggerProvider(Func<string, LogLevel, bool> filter, bool includeScopes)
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            _filter = filter;
            _includeScopes = includeScopes;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, CreateLoggerImplementation);
        }

        private JournalLogger CreateLoggerImplementation(string name)
        {
            return new JournalLogger(name, GetFilter(name), _includeScopes ? _scopeProvider : null);
        }

        private Func<string, LogLevel, bool> GetFilter(string name)
        {
            if (_filter != null)
            {
                return _filter;
            }

            return falseFilter;
        }

        public void Dispose()
        { }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}