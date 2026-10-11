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
        private readonly Queue<DateTime> _imageSpamMessages = new Queue<DateTime>();
        private readonly Queue<(DateTime Time, int Count)> _scamImages = new Queue<(DateTime, int)>();

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

        public bool IsValid()
        {
            var now = _timeProvider.Now();
            return (now - LastMessage).TotalMinutes <= 2 ||
                _scamImages.Any(x => (now - x.Time).TotalMinutes <= ImageSpamDecision.ScamWindowMinutes);
        }
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

        public ImageSpamDecision TrackImageSpam(bool hasTooManyImages, int matchedScamImages)
        {
            var now = _timeProvider.Now();
            while (_imageSpamMessages.Count > 0 &&
                (now - _imageSpamMessages.Peek()).TotalMinutes > ImageSpamDecision.SpamWindowMinutes)
                _imageSpamMessages.Dequeue();

            while (_scamImages.Count > 0 &&
                (now - _scamImages.Peek().Time).TotalMinutes > ImageSpamDecision.ScamWindowMinutes)
                _scamImages.Dequeue();

            if (hasTooManyImages)
                _imageSpamMessages.Enqueue(now);

            if (matchedScamImages > 0)
                _scamImages.Enqueue((now, matchedScamImages));

            return new ImageSpamDecision(
                hasTooManyImages ? _imageSpamMessages.Count : 0,
                matchedScamImages > 0 ? _scamImages.Sum(x => x.Count) : 0);
        }
    }
}
