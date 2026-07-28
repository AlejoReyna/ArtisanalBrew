using System;
using Nethereum.Hex.HexConvertors.Extensions;
class Program {
    static void Main() {
        byte[] b = new byte[32];
        string hex = b.ToHex(true);
        Console.WriteLine(hex);
        Console.WriteLine("Contains null? " + hex.Contains('\0'));
    }
}
