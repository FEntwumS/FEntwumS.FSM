using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OneWare.Essentials.ViewModels;

var method = typeof(ExtendedDocument).GetMethod("OnClose", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
var body = method.GetMethodBody()!;
var il = body.GetILAsByteArray()!;

var oneByte = new Dictionary<byte, OpCode>();
var twoByte = new Dictionary<byte, OpCode>();
foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
{
    if (f.GetValue(null) is OpCode op)
    {
        var value = (ushort)op.Value;
        if (value <= 0xFF) oneByte[(byte)value] = op;
        else if ((value & 0xFF00) == 0xFE00) twoByte[(byte)(value & 0xFF)] = op;
    }
}

int i = 0;
while (i < il.Length)
{
    int offset = i;
    OpCode op;
    byte code = il[i++];
    if (code == 0xFE) op = twoByte[il[i++]]; else op = oneByte[code];
    object? operand = null;
    int size = op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.ShortInlineR => 4,
        OperandType.InlineSwitch => BitConverter.ToInt32(il, i) * 4 + 4,
        _ => 0
    };
    if (size > 0)
    {
        if (op.OperandType == OperandType.InlineMethod)
        {
            int token = BitConverter.ToInt32(il, i);
            try { operand = method.Module.ResolveMethod(token); } catch { operand = $"methodToken=0x{token:X}"; }
        }
        else if (op.OperandType == OperandType.InlineField)
        {
            int token = BitConverter.ToInt32(il, i);
            try { operand = method.Module.ResolveField(token); } catch { operand = $"fieldToken=0x{token:X}"; }
        }
        else if (op.OperandType == OperandType.InlineType)
        {
            int token = BitConverter.ToInt32(il, i);
            try { operand = method.Module.ResolveType(token); } catch { operand = $"typeToken=0x{token:X}"; }
        }
        else if (op.OperandType == OperandType.InlineString)
        {
            int token = BitConverter.ToInt32(il, i);
            operand = method.Module.ResolveString(token);
        }
        else if (op.OperandType == OperandType.ShortInlineI)
        {
            operand = il[i];
        }
        else if (op.OperandType == OperandType.InlineI)
        {
            operand = BitConverter.ToInt32(il, i);
        }
        else if (op.OperandType == OperandType.ShortInlineBrTarget)
        {
            operand = (sbyte)il[i] + i + 1;
        }
        else if (op.OperandType == OperandType.InlineBrTarget)
        {
            operand = BitConverter.ToInt32(il, i) + i + 4;
        }
        else if (op.OperandType == OperandType.InlineVar)
        {
            operand = BitConverter.ToUInt16(il, i);
        }
        else if (op.OperandType == OperandType.ShortInlineVar)
        {
            operand = il[i];
        }
    }
    Console.WriteLine($"{offset:X4}: {op.Name} {operand}");
    i += size;
}
