﻿using System.Collections.Generic;
using System.Linq;

namespace GravyBot
{
    public interface IMessageRule
    {
        IAsyncEnumerable<OutboundIrcMessage> Respond(object incomingMessage);
    }

    public interface IMessageRule<TMessage> : IMessageRule
    {
        IAsyncEnumerable<OutboundIrcMessage> Respond(TMessage incomingMessage);
    }

    public abstract class MessageRuleBase<TMessage> : IMessageRule<TMessage>, IMessageRule
    {
        public abstract IAsyncEnumerable<OutboundIrcMessage> Respond(TMessage incomingMessage);

        public IAsyncEnumerable<OutboundIrcMessage> Respond(object incomingMessage) =>
            incomingMessage is TMessage message
            ? Respond(message)
            : EmptyResult();

        protected IAsyncEnumerable<OutboundIrcMessage> EmptyResult() => AsyncEnumerable.Empty<OutboundIrcMessage>();
    }
}
