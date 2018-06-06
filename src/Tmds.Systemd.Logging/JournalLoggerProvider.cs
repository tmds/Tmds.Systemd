using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tmds.Systemd.Logging
{
    class JournalLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentDictionary<string, JournalLogger> _loggers = new ConcurrentDictionary<string, JournalLogger>();
        private IExternalScopeProvider _scopeProvider;
        private readonly JournalLoggerOptions _options;

        public JournalLoggerProvider(IOptions<JournalLoggerOptions> options)
        {
            _options = options.Value;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _loggers.GetOrAdd(categoryName, CreateLoggerImplementation);
        }

        private JournalLogger CreateLoggerImplementation(string name)
        {
            return new JournalLogger(name, _scopeProvider, _options);
        }

        public void Dispose()
        { }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }
    }
}