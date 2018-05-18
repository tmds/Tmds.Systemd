using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tmds.Systemd.Logging;

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// Extension methods for SystemdJournal logger.
    /// </summary>
    public static class JournalLoggerExtensions
    {
        /// <summary>
        /// Adds a journal logger named 'SystemdJournal' to the factory.
        /// </summary>
        /// <param name="builder">The <see cref="ILoggingBuilder"/> to use.</param>
        public static ILoggingBuilder AddSystemdJournal(this ILoggingBuilder builder)
        {
            builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, JournalLoggerProvider>());
            return builder;
        }
    }
}