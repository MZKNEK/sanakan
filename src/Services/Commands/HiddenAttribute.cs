#pragma warning disable 1591

using System;
using System.Reflection;

namespace Sanakan.Services.Commands
{
    public class HiddenAttribute : Attribute
    {
        protected bool _isHidden;

        public HiddenAttribute()
        {
            _isHidden = true;
        }

        public bool IsHidden() => _isHidden;
    }
}