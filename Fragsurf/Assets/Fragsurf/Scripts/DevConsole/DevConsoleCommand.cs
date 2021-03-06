﻿using System;
using System.Collections.Generic;
using System.Reflection;

namespace Fragsurf
{
    public class DevConsoleCommand2 : DevConsoleCommand
    {

        private ParameterInfo[] _paramInfo;
        private MethodInfo _methodInfo;

        public DevConsoleCommand2(object owner, string name, string description, MethodInfo methodInfo)
            : base(owner, name, description, null)
        {
            _methodInfo = methodInfo;
            _paramInfo = _methodInfo.GetParameters();
        }

        protected override void _OnExecute(string[] args)
        {
            if(_paramInfo != null
                && _paramInfo.Length > 0)
            {
                if(args == null || args.Length != _paramInfo.Length)
                {
                    DevConsole.WriteLine(string.Join(" ", args));
                    DevConsole.WriteLine($"!> Got {args.Length} parameters, need {_paramInfo.Length}.  Wrap commands in quotes if it includes whitespace.  For example: bind BackQuote \"modal.toggle console\"");
                    return;
                }
                var parameters = new List<object>();
                for (int i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    var obj = Convert.ChangeType(arg, _paramInfo[i].ParameterType);
                    parameters.Add(obj);
                }
                _methodInfo.Invoke(Owner, parameters.ToArray());
            }
            else
            {
                _methodInfo.Invoke(Owner, null);
            }
        }

    }

    public class DevConsoleCommand : DevConsoleEntry
    {

        public DevConsoleCommand(object owner, string name, string description, Action<string[]> callback)
            : base(owner, name, description)
        {
            _callback = callback;
        }

        private Action<string[]> _callback;

        protected override void _OnExecute(string[] args)
        {
            _callback?.Invoke(args);
        }

    }
}

