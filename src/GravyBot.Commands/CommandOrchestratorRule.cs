﻿using GravyIrc.Messages;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace GravyBot.Commands
{
    /// <summary>
    /// Pipeline rule that handles commands
    /// </summary>
    public class CommandOrchestratorRule : IAsyncMessageRule<PrivateMessage>
    {
        private readonly IrcBotConfiguration configuration;
        private readonly ICommandProcessorProvider processorProvider;
        private readonly ICommandOrchestratorBuilder builder;
        private static readonly Dictionary<UserInvocation, DateTime> InvocationHistory = new Dictionary<UserInvocation, DateTime>();

        public CommandOrchestratorRule(IOptions<IrcBotConfiguration> options, ICommandOrchestratorBuilder builder, ICommandProcessorProvider processorProvider)
        {
            configuration = options.Value;
            this.builder = builder;
            this.processorProvider = processorProvider;
        }

        public bool Matches(PrivateMessage incomingMessage) => HasBinding(incomingMessage.Message);

        public async IAsyncEnumerable<IClientMessage> RespondAsync(PrivateMessage incomingMessage)
        {
            var pair = GetBinding(incomingMessage.Message);

            if (pair?.Value.Command != default)
            {
                var binding = pair.Value.Value;
                bool hasMainResponseBlocked = false;
                var key = new UserInvocation(incomingMessage.From, incomingMessage.To, binding.Command.CommandName);
                var now = DateTime.Now;

                if (binding.IsRateLimited)
                {
                    if (incomingMessage.IsChannelMessage && InvocationHistory.TryGetValue(key, out var previousInvocationTime) && now - previousInvocationTime < binding.RateLimitPeriod)
                    {
                        hasMainResponseBlocked = true;
                        var remainingTime = binding.RateLimitPeriod.Value - (now - previousInvocationTime);
                        yield return new NoticeMessage(incomingMessage.From, $"You must wait {remainingTime.ToFriendlyString()} before using the {binding.Command.CommandName} command again.");
                    }
                }

                if (binding.IsChannelOnly && !incomingMessage.IsChannelMessage)
                {
                    hasMainResponseBlocked = true;
                    yield return new NoticeMessage(incomingMessage.From, $"The {binding.Command.CommandName} command can only be used in channels.");
                }

                if (binding.IsDirectOnly && incomingMessage.IsChannelMessage)
                {
                    hasMainResponseBlocked = true;
                    yield return new NoticeMessage(incomingMessage.From, $"The {binding.Command.CommandName} command can only be used in direct messages.");
                }

                if (binding.HasPolicy && incomingMessage.IsChannelMessage)
                {
                    if (!builder.Policies.ContainsKey(binding.PolicyName))
                        throw new InvalidPolicyException(binding.PolicyName);

                    var policy = builder.Policies[binding.PolicyName];

                    if (policy.Blocks(incomingMessage))
                    {
                        hasMainResponseBlocked = true;
                        yield return new NoticeMessage(incomingMessage.From, $"Use of the {binding.Command.CommandName} command has been disabled in {incomingMessage.To}.");
                    }
                }

                if (!hasMainResponseBlocked)
                {
                    var bypassRatelimit = false;

                    if (binding.IsAsync)
                    {
                        if (binding.ProducesMultipleResponses)
                            await foreach (var r in Invoke<IAsyncEnumerable<IClientMessage>>(binding, incomingMessage))
                                yield return r;
                        else
                            yield return await Invoke<Task<IClientMessage>>(binding, incomingMessage);
                    }
                    else
                    {
                        if (binding.ProducesMultipleResponses)
                            foreach (var r in Invoke<IEnumerable<IClientMessage>>(binding, incomingMessage))
                                yield return r;
                        else
                            yield return Invoke<IClientMessage>(binding, incomingMessage);
                    }

                    if (!bypassRatelimit)
                        InvocationHistory[key] = now;

                    TResult Invoke<TResult>(CommandBinding binding, PrivateMessage message) =>
                        (TResult)binding.Method.Invoke(GetProcessor(binding, message), GetArguments(binding, message.Message));

                    CommandProcessor GetProcessor(CommandBinding binding, PrivateMessage message)
                    {
                        var processor = processorProvider.GetProcessor(binding, message);
                        processor.BypassRateLimit = () => bypassRatelimit = true;
                        return processor;
                    }
                }
            }
        }


        private object[] GetArguments(CommandBinding binding, string message)
        {
            var match = binding.Command.MatchingPattern.Match(message);

            // handle parameterless calls
            if (!match.Success)
                binding.Method.GetParameters().Select<ParameterInfo, object>(_ => null).ToArray();

            return binding.Method.GetParameters().Select(GetArgument).ToArray();

            object GetArgument(ParameterInfo paramInfo)
            {
                var indexInCommand = Array.IndexOf(binding.Command.ParameterNames, paramInfo.Name);
                if (indexInCommand > -1)
                {
                    var matchingGroup = match.Groups[indexInCommand + 1];
                    if (matchingGroup.Success)
                    {
                        var converter = TypeDescriptor.GetConverter(paramInfo.ParameterType);
                        if (converter.CanConvertFrom(typeof(string)))
                        {
                            try
                            {
                                return converter.ConvertFromString(matchingGroup.Value?.Trim());
                            }
                            catch (NotSupportedException)
                            {
                                return null;
                            }
                        }
                    }
                }
                return null;
            }
        }

        private bool HasBinding(string message)
        {
            if (message.StartsWith(configuration.CommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var commandSegment = message.Remove(0, configuration.CommandPrefix.Length);
                return builder.Bindings.Any(p => commandSegment.StartsWith(p.Key, StringComparison.OrdinalIgnoreCase));
            }
            return false;
        }

        private KeyValuePair<string, CommandBinding>? GetBinding(string message)
        {
            if (message.StartsWith(configuration.CommandPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var commandSegment = message.Remove(0, configuration.CommandPrefix.Length);
                return builder.Bindings.OrderByDescending(b => b.Key.Length).FirstOrDefault(p => commandSegment.StartsWith(p.Key, StringComparison.OrdinalIgnoreCase));
            }
            return null;
        }

        private struct UserInvocation
        {
            public UserInvocation(string nick, string channel, string command)
            {
                Nick = nick;
                Channel = channel;
                Command = command;
            }

            public string Nick;
            public string Channel;
            public string Command;
        }
    }
}
