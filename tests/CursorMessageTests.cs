using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Windows.Forms;

namespace CodexUsageOverlay
{
    // Inspect the real compiled handlers without constructing forms or touching
    // the desktop cursor. Control.Cursor's setter sends WM_SETCURSOR on Windows;
    // calling it from either handler causes synchronous recursion (0xc00000fd).
    internal static class CursorMessageTests
    {
        internal static void DoesNotReenterControlCursor(Assembly assembly, string typeName)
        {
            Type type = assembly.GetType(typeName, true);
            MethodInfo handler = type.GetMethod("WndProc",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (handler == null) throw new InvalidOperationException("Missing real WndProc: " + typeName);
            bool setsCurrent = false;
            foreach (MethodBase called in Calls(handler))
            {
                if (called.Name == "set_Cursor" && called.DeclaringType != null &&
                    typeof(Control).IsAssignableFrom(called.DeclaringType))
                    throw new InvalidOperationException(typeName + " writes Control.Cursor from WndProc (recursive WM_SETCURSOR)");
                if (called.DeclaringType == typeof(Cursor) && called.Name == "set_Current")
                    setsCurrent = true;
            }
            if (!setsCurrent) throw new InvalidOperationException(typeName + " lost direct current-cursor feedback");
        }

        private static IEnumerable<MethodBase> Calls(MethodInfo method)
        {
            Dictionary<short, OpCode> codes = new Dictionary<short, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (field.FieldType == typeof(OpCode))
                {
                    OpCode code = (OpCode)field.GetValue(null);
                    codes[code.Value] = code;
                }
            byte[] bytes = method.GetMethodBody().GetILAsByteArray();
            int offset = 0;
            while (offset < bytes.Length)
            {
                short value = bytes[offset++];
                if (value == 0xfe) value = unchecked((short)(0xfe00 | bytes[offset++]));
                OpCode code;
                if (!codes.TryGetValue(value, out code)) throw new InvalidOperationException("Unknown IL opcode");
                if (code == OpCodes.Call || code == OpCodes.Callvirt)
                    yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset));
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar: offset += 1; break;
                    case OperandType.InlineVar: offset += 2; break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineField:
                    case OperandType.InlineI:
                    case OperandType.InlineMethod:
                    case OperandType.InlineSig:
                    case OperandType.InlineString:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                    case OperandType.ShortInlineR: offset += 4; break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR: offset += 8; break;
                    case OperandType.InlineSwitch:
                        offset += 4 + 4 * BitConverter.ToInt32(bytes, offset); break;
                    default: throw new InvalidOperationException("Unsupported IL operand " + code.OperandType);
                }
                if (offset > bytes.Length) throw new InvalidOperationException("Truncated IL operand");
            }
        }
    }
}
