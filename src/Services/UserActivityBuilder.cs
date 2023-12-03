#pragma warning disable 1591

using System;
using System.Collections.Generic;
using Sanakan.Database.Models;
using Sanakan.Extensions;
using Sanakan.Services.Time;

namespace Sanakan.Services
{
    public class UserActivityBuilder
    {
        private readonly ISystemTime _time;
        private UserActivity _activity;
        private List<string> _misc;
        private string _cardText;
        private bool _typeWasSet;

        public UserActivityBuilder(ISystemTime time)
        {
            _time = time;
            _cardText = "";
            _typeWasSet = false;
            _misc = new List<string>();
            _activity = new UserActivity();
        }

        public UserActivityBuilder WithUser(User user, Discord.IUser dUser)
        {
            _activity.UserId = dUser?.Id ?? 0;
            return WithUser(user, dUser.GetUserNickInGuild());
        }

        public UserActivityBuilder WithUser(User user, string name = "")
        {
            _activity.ShindenId = user?.Shinden ?? 0;
            if (_activity.UserId == 0)
            {
                _activity.UserId = user?.Id ?? 0;
            }
            if (!string.IsNullOrEmpty(name))
            {
                _misc.Add($"u:{name}");
            }
            return this;
        }

        public UserActivityBuilder WithCard(Card card)
        {
            _activity.TargetId = card.Id;
            _cardText = $"<w@{card.Id}> <c@{card.Character}>";
            if (card.WhoWantsCount > 1)
            {
                _cardText = $"({card.WhoWantsCount}) {_cardText}";
            }
            if (_activity.UserId == 0 && card.GameDeck != null && card.GameDeck.User != null)
            {
                _activity.ShindenId = card.GameDeck.User.Shinden;
            }
            if (_activity.UserId == 0)
            {
                _activity.UserId = card.GameDeckId;
            }
            _misc.Add($"w:[{card.Id}] {card.GetCardRealRarity()}");
            _misc.Add($"wp:H{card.GetHealthWithPenalty()} A{card.GetAttackWithBonus()} D{card.GetDefenceWithBonus()}");
            _misc.Add($"c:{card.Name.Trim()}");
            _misc.Add($"kc:{card.WhoWantsCount}");
            return this;
        }

        public UserActivityBuilder AddMisc(string misc)
        {
            _misc.Add(misc);
            return this;
        }

        public UserActivityBuilder WithType(ActivityType type)
        {
            switch (type)
            {
                case ActivityType.AcquiredCardSSS:
                case ActivityType.AcquiredCardKC:
                case ActivityType.AcquiredCardHighKC:
                case ActivityType.AcquiredCardWishlist:
                case ActivityType.AcquiredCarcUltimate:
                case ActivityType.UsedScalpel:
                case ActivityType.CreatedYato:
                case ActivityType.CreatedYami:
                case ActivityType.CreatedRaito:
                case ActivityType.CreatedSSS:
                case ActivityType.CreatedUltiamte:
                case ActivityType.AddedToWishlistCard:
                {
                    if (string.IsNullOrEmpty(_cardText))
                    {
                        throw new Exception("Missing card text!");
                    }
                }
                break;
            }
            _typeWasSet = true;
            _activity.Type = type;
            switch (_activity.Type)
            {
                case ActivityType.LevelUp:
                    _activity.Text = $"Użytkownik zdobył {_activity.TargetId} poziom na discordzie.";
                break;
                case ActivityType.Muted:
                    _activity.Text = $"Użytkownik został wyciszony na discordzie.";
                break;
                case ActivityType.Banned:
                    _activity.Text = $"Użytkownik został zbanowany na discordzie.";
                break;
                case ActivityType.Kicked:
                    _activity.Text = $"Użytkownik został wyrzucony z discorda.";
                break;
                case ActivityType.Connected:
                    _activity.Text = $"Użytkownik połączył konto shinden z kontem discord.";
                break;
                case ActivityType.LotteryStarted:
                    _activity.Text = "Rozpoczęła się loteria na discordzie!";
                break;
                case ActivityType.WonLottery:
                    _activity.Text = $"Użytkownik wygrał loterię na discordzie.";
                break;
                case ActivityType.AcquiredCardSSS:
                    _activity.Text = $"Użytkownik zdobył kartę SSS: {_cardText}";
                break;
                case ActivityType.AcquiredCardKC:
                    _activity.Text = $"Użytkownik zdobył kartę z KC: {_cardText}";
                break;
                case ActivityType.AcquiredCardHighKC:
                    _activity.Text = $"Użytkownik zdobył kartę z dużą liczbą KC: {_cardText}";
                break;
                case ActivityType.AcquiredCardWishlist:
                    _activity.Text = $"Użytkownik zdobył kartę ze swojej listy życzeń: {_cardText}";
                break;
                case ActivityType.AcquiredCarcUltimate:
                    _activity.Text = $"Użytkownik zdobył kartę ultimate: {_cardText}";
                break;
                case ActivityType.UsedScalpel:
                    _activity.Text = $"Użytkownik użył skalpel na karcie: {_cardText}";
                break;
                case ActivityType.CreatedYato:
                    _activity.Text = $"Użytkownik ustawił charakter Yato na karcie: {_cardText}";
                break;
                case ActivityType.CreatedYami:
                    _activity.Text = $"Użytkownik ustawił charakter Yami na karcie: {_cardText}";
                break;
                case ActivityType.CreatedRaito:
                    _activity.Text = $"Użytkownik ustawił charakter Raito na karcie: {_cardText}";
                break;
                case ActivityType.CreatedSSS:
                    _activity.Text = $"Użytkownik zwiększył jakośc karty do SSS: {_cardText}";
                break;
                case ActivityType.CreatedUltiamte:
                    _activity.Text = $"Użytkownik utworzył kartę ultimate: {_cardText}";
                break;
                case ActivityType.AddedToWishlistCharacter:
                    _activity.Text = $"Użytkownik dodał postać do listy życzeń: <c@{_activity.TargetId}>";
                break;
                case ActivityType.AddedToWishlistTitle:
                    _activity.Text = $"Użytkownik dodał tytuł do listy życzeń: <t@{_activity.TargetId}>";
                break;
                case ActivityType.AddedToWishlistCard:
                    _activity.Text = $"Użytkownik dodał kartę do listy życzeń: {_cardText}";
                break;
            }
            return this;
        }

        public UserActivityBuilder WithType(ActivityType type, ulong target) => SetTarget(target).WithType(type);

        public UserActivityBuilder SetTarget(ulong target)
        {
            _activity.TargetId = target;
            return this;
        }

        public UserActivityBuilder SetText(string text)
        {
            _activity.Text = text;
            return this;
        }

        public UserActivity Build()
        {
            if (!_typeWasSet)
            {
                throw new Exception("Missing type!");
            }

            switch (_activity.Type)
            {
                case ActivityType.LevelUp:
                case ActivityType.AddedToWishlistCharacter:
                case ActivityType.AddedToWishlistTitle:
                case ActivityType.AddedToWishlistCard:
                case ActivityType.AcquiredCardSSS:
                case ActivityType.AcquiredCardKC:
                case ActivityType.AcquiredCardHighKC:
                case ActivityType.AcquiredCardWishlist:
                case ActivityType.AcquiredCarcUltimate:
                case ActivityType.UsedScalpel:
                case ActivityType.CreatedYato:
                case ActivityType.CreatedYami:
                case ActivityType.CreatedRaito:
                case ActivityType.CreatedSSS:
                case ActivityType.CreatedUltiamte:
                {
                    if (_activity.UserId == 0)
                    {
                        throw new Exception("Missing user id!");
                    }
                    if (_activity.TargetId == 0)
                    {
                        throw new Exception("Missing target id!");
                    }
                }
                break;

                case ActivityType.Connected:
                case ActivityType.WonLottery:
                case ActivityType.Muted:
                case ActivityType.Banned:
                case ActivityType.Kicked:
                {
                    if (_activity.UserId == 0)
                    {
                        throw new Exception("Missing user id!");
                    }
                }
                break;

                case ActivityType.LotteryStarted:
                break;
            }

            if (string.IsNullOrEmpty(_activity.Text))
            {
                throw new Exception("Missing text!");
            }

            _activity.Misc = string.Join(';', _misc);
            _activity.Date = _time.Now();
            return _activity;
        }
    }
}