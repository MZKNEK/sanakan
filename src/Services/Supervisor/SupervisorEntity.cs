#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Linq;
using Sanakan.Services.Time;

namespace Sanakan.Services.Supervisor
{
    public class SupervisorEntity
    {
        private const int MAX_TRACKED_MESSAGES = 100;
        private readonly ISystemTime _timeProvider;

        public List<SupervisorMessage> Messages { get; private set; }
        public DateTime LastMessage { get; private set; }
        public int TotalMessages { get; private set; }
        private DateTime LastImageSpamMessage { get; set; }
        private int ImageSpamMessages { get; set; }

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
            LastImageSpamMessage = LastMessage;
            TotalMessages = 0;
            ImageSpamMessages = 0;
        }

        public SupervisorMessage Get(string content)
        {
            Messages.RemoveAll(x => !x.IsValid());

            var msg = Messages.FirstOrDefault(x => x.Content == content);
            if (msg == null)
            {
                if (Messages.Count >= MAX_TRACKED_MESSAGES)
                    Messages.RemoveAt(0);

                msg = new SupervisorMessage(content, _timeProvider, 0);
                Messages.Add(msg);
            }
            return msg;
        }

        public bool IsValid() => (_timeProvider.Now() - LastMessage).TotalMinutes <= 2;
        public void Add(SupervisorMessage message)
        {
            Messages.RemoveAll(x => !x.IsValid());
            if (Messages.Count >= MAX_TRACKED_MESSAGES)
                Messages.RemoveAt(0);

            Messages.Add(message);
        }

        public int Inc()
        {
            if ((_timeProvider.Now() - LastMessage).TotalSeconds > 5)
                TotalMessages = 0;

            LastMessage = _timeProvider.Now();

            return ++TotalMessages;
        }

        public int IncImageSpam(int amount = 1)
        {
            var now = _timeProvider.Now();
            if ((now - LastImageSpamMessage).TotalMinutes > 2)
                ImageSpamMessages = 0;

            LastImageSpamMessage = now;
            ImageSpamMessages += amount;
            return ImageSpamMessages;
        }
    }
}
