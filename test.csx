using System;
byte[] b = new byte[32];
string hex = BitConverter.ToString(b).Replace("-", "").ToLower();
Console.WriteLine(hex);
Console.WriteLine("Contains null? " + hex.Contains('\0'));
