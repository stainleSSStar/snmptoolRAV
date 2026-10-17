using System;
using System.IO;
using System.Reflection;

namespace SnmptoolRAV {
    static class Embedded {
        static readonly Lazy<Assembly> library=new Lazy<Assembly>(()=>Assembly.Load(ReadBytes("SharpSnmpLib.dll")));
        public static void Initialize(){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs args){return new AssemblyName(args.Name).Name=="SharpSnmpLib"?library.Value:null;};}
        static byte[] ReadBytes(string name){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){if(stream==null)throw new FileNotFoundException("Embedded resource is missing: "+name);using(var memory=new MemoryStream()){stream.CopyTo(memory);return memory.ToArray();}}}
        public static string Notices(){return System.Text.Encoding.UTF8.GetString(ReadBytes("ThirdPartyNotices.txt"));}
    }
}
