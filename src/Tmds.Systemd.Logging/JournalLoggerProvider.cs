using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Tmds.Systemd.Logging
{
    [ProviderAlias("SystemdJournal")]
    public class JournalLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentDictionary<string, JournalLogger> _loggers = new ConcurrentDictionary<string, JournalLogger>();
        private IExternalScopeProvider _scopeProvider;

        public JournalLoggerProvider()
        { }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, CreateLoggerImplementation);
        }

        private JournalLogger CreateLoggerImplementation(string name)
        {
            return new JournalLogger(name, _scopeProvider);
        }

        public void Dispose()
        { }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}