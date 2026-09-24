using System;
using System.Reflection;

class Program {
    static void Main() {
        string dir = @"C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program_newmod\KSP_x64_Data\Managed";
        Assembly.LoadFrom(dir + @"\UnityEngine.dll");
        Assembly.LoadFrom(dir + @"\UnityEngine.CoreModule.dll");
        Assembly.LoadFrom(dir + @"\Assembly-CSharp.dll");
        var asm = Assembly.LoadFrom(dir + @"\Assembly-CSharp.dll");
        var type = asm.GetType("KSP.UI.Screens.Flight.NavBall");
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)) {
            var body = m.GetMethodBody();
            if (body == null) continue;
            byte[] bytes = body.GetILAsByteArray();
            for (int i = 0; i < bytes.Length - 4; i++) {
                byte b = bytes[i];
                if (b == 0x7D) { // stfld
                    int token = BitConverter.ToInt32(bytes, i + 1);
                    try {
                        var f = m.Module.ResolveMember(token);
                        if (f.Name == "vectorUnitScale") {
                            Console.WriteLine("vectorUnitScale is set in: " + m.Name);
                        }
                    } catch {}
                }
            }
        }
    }
}
