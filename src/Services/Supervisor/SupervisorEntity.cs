#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Services.Time;

namespace Sanakan.Services.Supervisor
{
    public class SupervisorEntity
    {
        private readonly ISystemTime _timeProvider;

        public List<SupervisorMessage> Messages { get; private set; }
        public DateTime LastMessage { get; private set; }
        public int TotalMessages { get; private set; }

        public SupervisorEntity(string contentOfFirstMessage, ISystemTime timeProvider) : this(timeProvider)
        {
            TotalMessages = 1;
            Messages.Add(new SupervisorMessage(contentOfFirstMessage, timeProvider));
        }

        public SupervisorEntity(ISystemTime timeProvider)
        {
            _timeProvider = timeProvider;

            Messages = new List<SupervisorMessage>();
            LastMessage = _timeProvider.Now();
            TotalMessages = 0;
        }

        public SupervisorMessage Get(string content)
        {
            var msg = Messages.FirstOrDefault(x => x.Content == content);
            if (msg == null)
            {
                msg = new SupervisorMessage(content, _timeProvider, 0);
                Messages.Add(msg);
            }
            return msg;
        }

        public bool IsValid() => (_timeProvider.Now() - LastMessage).TotalMinutes <= 2;
        public void Add(SupervisorMessage message) => Messages.Add(message);
        public int Inc()
        {
            if ((_timeProvider.Now() - LastMessage).TotalSeconds > 5)
                TotalMessages = 0;

            LastMessage = _timeProvider.Now();

            return ++TotalMessages;
        }
    }
}
