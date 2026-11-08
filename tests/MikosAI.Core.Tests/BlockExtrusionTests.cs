using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace MikosAI.Core.Tests
{
    public class BlockExtrusionTests
    {
        [Fact]
        public void blind_boss_emits_correct_actual_parameter_order()
        {
            var helper=AddinAssembly.Tipe("MikosAI.Addin.BlockExtrusion").GetMethod("Create",BindingFlags.Static|BindingFlags.NonPublic)!;
            var managerType=helper.GetParameters()[0].ParameterType;
            var native=managerType.GetMethod("FeatureExtrusion2")!;
            var parameters=native.GetParameters();
            var assembly=AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CaptureExtrusion"+Guid.NewGuid().ToString("N")),AssemblyBuilderAccess.Run);
            var builder=assembly.DefineDynamicModule("main").DefineType("Capture",TypeAttributes.Public);
            builder.AddInterfaceImplementation(managerType);
            var captured=builder.DefineField("Arguments",typeof(object[]),FieldAttributes.Public|FieldAttributes.Static);
            var method=builder.DefineMethod(native.Name,MethodAttributes.Public|MethodAttributes.Virtual,native.ReturnType,parameters.Select(p=>p.ParameterType).ToArray());
            var il=method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4,parameters.Length); il.Emit(OpCodes.Newarr,typeof(object)); il.Emit(OpCodes.Stsfld,captured);
            for(var i=0;i<parameters.Length;i++)
            {
                il.Emit(OpCodes.Ldsfld,captured); il.Emit(OpCodes.Ldc_I4,i); il.Emit(OpCodes.Ldarg,i+1);
                if(parameters[i].ParameterType.IsValueType) il.Emit(OpCodes.Box,parameters[i].ParameterType);
                il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
            builder.DefineMethodOverride(method,native);
            // Build NoPIA dapat memuat member interface lain yang dipakai writer.
            // Fake menolak semuanya kecuali panggilan yang sedang diuji.
            foreach(var member in managerType.GetInterfaces().Concat(new[]{managerType}).SelectMany(t=>t.GetMethods())
                .GroupBy(m=>m.ToString()).Select(g=>g.First()).Where(m=>m.Name!=native.Name))
            {
                var unused=builder.DefineMethod(member.Name,MethodAttributes.Public|MethodAttributes.Virtual,
                    member.ReturnType,member.GetParameters().Select(p=>p.ParameterType).ToArray());
                var body=unused.GetILGenerator();
                body.Emit(OpCodes.Newobj,typeof(NotSupportedException).GetConstructor(Type.EmptyTypes)!);
                body.Emit(OpCodes.Throw);
                builder.DefineMethodOverride(unused,member);
            }
            var type=builder.CreateType()!;
            helper.Invoke(null,new[]{Activator.CreateInstance(type),(object)0.0123});
            var args=(object[])type.GetField("Arguments")!.GetValue(null)!;
            // Oracle: urutan PIA SW2025 SP01.1, blind satu arah normal, tanpa
            // draft/offset, depth hanya D1. Salah menaruh D1 ke D2 harus merah.
            Assert.Equal(new object[]{true,false,false,0,0,0.0123,0d,false,false,false,false,0d,0d,false,false,false,false,true,true,true,0,0d,false},args);
        }
    }
}
