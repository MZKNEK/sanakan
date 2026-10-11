using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Serialization;
using Discord;
using Discord.WebSocket;

namespace Artifacts
{
    internal static class SocketTestObjects
    {
        public static SocketGuildUser User(ulong id)
        {
            var user = (SocketGuildUser)FormatterServices.GetUninitializedObject(typeof(SocketGuildUser));
            SetField(user, "<Id>k__BackingField", id);
            SetField(user, "<Nickname>k__BackingField", $"user{id}");
            return user;
        }

        public static SocketUserMessage Message(SocketGuildUser user, string text, ISocketMessageChannel channel = null)
        {
            var message = (SocketUserMessage)FormatterServices.GetUninitializedObject(typeof(SocketUserMessage));
            SetField(message, "<Author>k__BackingField", user);
            SetField(message, "<Content>k__BackingField", text);
            SetField(message, "<Channel>k__BackingField", channel);
            SetField(message, "_tags", ImmutableArray<ITag>.Empty);
            return message;
        }

        private static void SetField(object target, string name, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }
            throw new InvalidOperationException(name);
        }
    }
}
