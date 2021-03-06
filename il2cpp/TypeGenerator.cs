﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using dnlib.DotNet;

namespace il2cpp
{
	internal class TypeGenerator
	{
		private readonly GeneratorContext GenContext;
		private readonly TypeX CurrType;

		public TypeGenerator(GeneratorContext genContext, TypeX tyX)
		{
			GenContext = genContext;
			CurrType = tyX;
		}

		public CompileUnit Generate()
		{
			CompileUnit unit = new CompileUnit();
			unit.Name = GenContext.GetTypeName(CurrType);

			// 重排字段
			var fields = LayoutFields(out var sfields);

			// 生成类结构
			CodePrinter prtDecl = new CodePrinter();

			if (!CurrType.IsEnumType)
			{
				prtDecl.AppendFormatLine("// {0}", CurrType.GetNameKey());
				var baseType = CurrType.BaseType;

				// 值类型不继承任何基类
				if (CurrType.IsValueType)
					baseType = null;

				// 接口类型继承 object
				if (baseType == null && CurrType.Def.IsInterface)
					baseType = GenContext.TypeMgr.GetTypeByName("Object");

				if (baseType != null)
				{
					string strBaseTypeName = GenContext.GetTypeName(baseType);
					unit.DeclDepends.Add(strBaseTypeName);

					prtDecl.AppendFormatLine("struct {0} : {1}",
						GenContext.GetTypeName(CurrType),
						strBaseTypeName);
				}
				else
				{
					prtDecl.AppendFormatLine("struct {0}",
						GenContext.GetTypeName(CurrType));
				}

				prtDecl.AppendLine("{");
				++prtDecl.Indents;

				// 生成对象内置字段
				if (CurrType.IsArrayType)
				{
					var arrayInfo = CurrType.ArrayInfo;
					if (arrayInfo.IsSZArray)
						prtDecl.AppendLine("int32_t Length;");
					else
					{
						uint rank = arrayInfo.Rank;
						for (int i = 0; i < rank; ++i)
							prtDecl.AppendFormatLine("int32_t LowerBound{0};\nint32_t Size{0};", i);
					}
				}
				else
				{
					string nameKey = CurrType.GetNameKey();
					if (nameKey == "Object")
					{
						prtDecl.AppendLine("uint32_t TypeID;");
					}
					else if (nameKey == "System.Array")
					{
						prtDecl.AppendLine("int32_t Rank;");
					}
				}

				// 生成字段
				foreach (var fldX in fields)
				{
					RefValueTypeDecl(unit, fldX.FieldType);

					prtDecl.AppendLine("// " + fldX.GetReplacedNameKey());
					prtDecl.AppendFormatLine("{0} {1};",
						GenContext.GetTypeName(fldX.FieldType),
						GenContext.GetFieldName(fldX));
				}

				--prtDecl.Indents;
				prtDecl.AppendLine("};");
			}

			CodePrinter prtImpl = new CodePrinter();
			// 生成静态字段
			foreach (var sfldX in sfields)
			{
				RefValueTypeDecl(unit, sfldX.FieldType);

				string fldDecl = string.Format("{0} {1};",
					GenContext.GetTypeName(sfldX.FieldType),
					GenContext.GetFieldName(sfldX));

				prtDecl.AppendFormatLine("// {0} -> {1}", sfldX.DeclType.GetNameKey(), sfldX.GetReplacedNameKey());
				prtDecl.AppendLine("extern " + fldDecl);
				prtImpl.AppendLine(fldDecl);
			}

			// 生成方法
			foreach (MethodX metX in CurrType.Methods)
			{
				var metGen = new MethodGenerator(GenContext, metX);
				metGen.Generate();

				prtDecl.AppendFormatLine("// {0} -> {1}", metX.DeclType.GetNameKey(), metX.GetReplacedNameKey());
				prtDecl.Append(metGen.DeclCode);
				unit.DeclDepends.UnionWith(metGen.DeclDepends);

				prtImpl.Append(metGen.ImplCode);
				unit.ImplDepends.UnionWith(metGen.ImplDepends);
			}

			unit.DeclCode = prtDecl.ToString();
			unit.ImplCode = prtImpl.ToString();

			return unit;
		}

		private void RefValueTypeDecl(CompileUnit unit, TypeSig tySig)
		{
			if (!Helper.IsValueType(tySig))
				return;

			TypeX tyX = GenContext.GetTypeBySig(tySig);
			if (tyX != null)
				unit.DeclDepends.Add(GenContext.GetTypeName(tyX));
		}

		private List<FieldX> LayoutFields(out List<FieldX> sfields)
		{
			sfields = new List<FieldX>();

			List<FieldX> fields = new List<FieldX>();
			foreach (var fldX in CurrType.Fields)
			{
				if (fldX.IsStatic)
					sfields.Add(fldX);
				else
					fields.Add(fldX);
			}

			var layoutType = CurrType.Def.Layout;

			if (layoutType == TypeAttributes.AutoLayout)
			{
				fields.Sort((lhs, rhs) =>
					GenContext.GetTypeLayoutOrder(lhs.FieldType).CompareTo(GenContext.GetTypeLayoutOrder(rhs.FieldType)));
			}
			else if (layoutType == TypeAttributes.SequentialLayout)
			{
				fields.Sort((lhs, rhs) => lhs.Def.Rid.CompareTo(rhs.Def.Rid));

				for (int i = 0; i < fields.Count - 1; ++i)
					Debug.Assert(fields[i].Def.Rid < fields[i + 1].Def.Rid);
			}
			else if (layoutType == TypeAttributes.ExplicitLayout)
			{
				throw new NotImplementedException();
			}

			return fields;
		}
	}
}
