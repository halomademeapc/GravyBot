﻿using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GravyBot
{
    public class MessageQueueService
    {
        private const int MAX_HISTORY = 300;

        private List<OutboundIrcMessage> queuedMessages = new List<OutboundIrcMessage>();
        private List<object> messageHistory = new List<object>();
        private List<OutboundIrcMessage> outputHistory = new List<OutboundIrcMessage>();
        private readonly IServiceProvider serviceProvider;

        public event EventHandler MessageAdded;

        public MessageQueueService(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public IEnumerable<OutboundIrcMessage> ViewAll() => queuedMessages;
        public IEnumerable<object> GetHistory() => messageHistory;
        public IEnumerable<OutboundIrcMessage> GetOutputHistory() => outputHistory;

        private Task ApplyRules<TMessage>(TMessage message)
        {
            var rules = serviceProvider.GetServices<IMessageRule>();
            var matchingRuleType = typeof(IMessageRule<>).MakeGenericType(message.GetType());
            var applicableRules = rules.Where(r => matchingRuleType.IsAssignableFrom(r.GetType()));
            var ruleTasks = applicableRules.Select(ExecuteRuleAsync);

            return Task.WhenAll(ruleTasks);

            async Task ExecuteRuleAsync(IMessageRule rule)
            {
                try
                {
                    await foreach (var responseMessage in rule.Respond(message))
                    {
                        AddOutput(responseMessage);
                    }
                }
                catch (Exception e)
                {
                    Push(new ExceptionMessage(e));
                }

            }
        }

        public List<OutboundIrcMessage> PopAll()
        {
            var messages = queuedMessages;
            queuedMessages = new List<OutboundIrcMessage>();
            return messages;
        }

        public void Remove(OutboundIrcMessage message) => queuedMessages.Remove(message);

        private void AddOutput(OutboundIrcMessage message)
        {
            queuedMessages.Add(message);
            outputHistory.Add(message);
            TrimOutputHistory();
            OnMessageAdded(EventArgs.Empty);
        }

        public void Push<TMessage>(TMessage message)
        {
            messageHistory.Add(message);
            TrimHistory();
            _ = Task.Run(() => ApplyRules(message));
        }

        public void PushRaw(OutboundIrcMessage message) => AddOutput(message);

        private void OnMessageAdded(EventArgs e)
        {
            MessageAdded?.Invoke(this, e);
        }

        private void TrimHistory()
        {
            var overflow = messageHistory.Count - MAX_HISTORY;
            if (overflow > 0)
            {
                messageHistory = messageHistory.Skip(overflow).Take(MAX_HISTORY).ToList();
            }
        }

        private void TrimOutputHistory()
        {
            var overflow = outputHistory.Count - MAX_HISTORY;
            if (overflow > 0)
            {
                outputHistory = outputHistory.Skip(overflow).Take(MAX_HISTORY).ToList();
            }
        }
    }
}
