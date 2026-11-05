using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Ratatoskr
{
    internal sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _first = new Stack<bool>();

        public Json Obj() { Sep(); _sb.Append('{'); _first.Push(true); return this; }
        public Json EndObj() { _sb.Append('}'); _first.Pop(); return this; }
        public Json Arr() { Sep(); _sb.Append('['); _first.Push(true); return this; }
        public Json EndArr() { _sb.Append(']'); _first.Pop(); return this; }

        public Json Key(string key)
        {
            Sep();
            Str(key);
            _sb.Append(':');
            _skipNext = true;
            return this;
        }

        public Json Val(string v) { Sep(); if (v == null) _sb.Append("null"); else Str(v); return this; }
        public Json Val(long v) { Sep(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public Json Val(double v) { Sep(); _sb.Append(System.Math.Round(v, 2).ToString(CultureInfo.InvariantCulture)); return this; }
        public Json Val(bool v) { Sep(); _sb.Append(v ? "true" : "false"); return this; }
        public Json Null() { Sep(); _sb.Append("null"); return this; }
        public Json Raw(string json) { Sep(); _sb.Append(json); return this; }

        public Json Pos(Vector3 p) => Arr().Val((double)p.x).Val((double)p.y).Val((double)p.z).EndArr();

        public Json Prop(string k, string v) => Key(k).Val(v);
        public Json Prop(string k, long v) => Key(k).Val(v);
        public Json Prop(string k, double v) => Key(k).Val(v);
        public Json Prop(string k, bool v) => Key(k).Val(v);

        private bool _skipNext;

        private void Sep()
        {
            if (_skipNext) { _skipNext = false; return; }
            if (_first.Count == 0) return;
            if (_first.Peek()) { _first.Pop(); _first.Push(false); }
            else _sb.Append(',');
        }

        private void Str(string s)
        {
            _sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) _sb.AppendFormat("\\u{0:x4}", (int)c);
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }

        public override string ToString() => _sb.ToString();
    }
}
