using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tmds.Systemd.Logging;
using Tmds.Systemd;

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
        public static ILoggingBuilder AddJournal(this ILoggingBuilder builder)
        {
            if (Journal.IsAvailable)
            {
                builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, JournalLoggerProvider>());
            }
            return builder;
        }

        /// <summary>
        /// Adds a journal logger named 'SystemdJournal' to the factory.
        /// </summary>
        /// <param name="builder">The <see cref="ILoggingBuilder"/> to use.</param>
        /// <param name="configure"></param>
        public static ILoggingBuilder AddJournal(this ILoggingBuilder builder, Action<JournalLoggerOptions> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            builder.AddJournal();
            builder.Services.Configure(configure);

            return builder;
        }
    }
}