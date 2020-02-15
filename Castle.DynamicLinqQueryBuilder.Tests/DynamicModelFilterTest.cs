using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace Castle.DynamicLinqQueryBuilder.Tests
{
    public class BoundedReferenceDataActionItem
    {
        public string ReferencedObjectId { get; set; }
        public int FormId { get; set; }
        public int Mode { get; set; }
        public int ComponentStateId { get; set; }
        public int StateId { get; set; }
        public int ComponentId { get; set; }
        public int ComponentEventId { get; set; }
        public dynamic DataItem { get; set; }
    }

    public static class MyTypeBuilder
    {
        public static object CreateNewObject(List<dynamic> yourListOfFields, out Type type)
        {
            var myType = CompileResultType(yourListOfFields);
            var myObject = Activator.CreateInstance(myType);

            type = myType;

            return myObject;
        }
        public static Type CompileResultType(List<dynamic> yourListOfFields)
        {
            TypeBuilder tb = GetTypeBuilder();
            ConstructorBuilder constructor = tb.DefineDefaultConstructor(MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName);

            // NOTE: assuming your list contains Field objects with fields FieldName(string) and FieldType(Type)
            foreach (var field in yourListOfFields)
            {
                if (field.FieldType!= typeof(ExpandoObject))
                {
                    CreateProperty(tb, field.FieldName, field.FieldType);
                }
                else
                {
                    CreateProperty(tb, field.FieldName, typeof(object));
                }
            } 

            Type objectType = tb.CreateType();
            return objectType;
        }

        private static TypeBuilder GetTypeBuilder()
        {
            var typeSignature = "MyDynamicType"+new Random().Next(0,100).ToString();
            var an = new AssemblyName(typeSignature);
            AssemblyBuilder assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
            TypeBuilder tb = moduleBuilder.DefineType(typeSignature,
                    TypeAttributes.Public |
                    TypeAttributes.Class |
                    TypeAttributes.AutoClass |
                    TypeAttributes.AnsiClass |
                    TypeAttributes.BeforeFieldInit |
                    TypeAttributes.AutoLayout,
                    null);
            return tb;
        }

        private static void CreateProperty(TypeBuilder tb, string propertyName, Type propertyType)
        {
            FieldBuilder fieldBuilder = tb.DefineField("_" + propertyName, propertyType, FieldAttributes.Private);

            PropertyBuilder propertyBuilder = tb.DefineProperty(propertyName, PropertyAttributes.HasDefault, propertyType, null);
            MethodBuilder getPropMthdBldr = tb.DefineMethod("get_" + propertyName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propertyType, Type.EmptyTypes);
            ILGenerator getIl = getPropMthdBldr.GetILGenerator();

            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, fieldBuilder);
            getIl.Emit(OpCodes.Ret);

            MethodBuilder setPropMthdBldr =
                tb.DefineMethod("set_" + propertyName,
                  MethodAttributes.Public |
                  MethodAttributes.SpecialName |
                  MethodAttributes.HideBySig,
                  null, new[] { propertyType });

            ILGenerator setIl = setPropMthdBldr.GetILGenerator();
            Label modifyProperty = setIl.DefineLabel();
            Label exitSet = setIl.DefineLabel();

            setIl.MarkLabel(modifyProperty);
            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, fieldBuilder);

            setIl.Emit(OpCodes.Nop);
            setIl.MarkLabel(exitSet);
            setIl.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getPropMthdBldr);
            propertyBuilder.SetSetMethod(setPropMthdBldr);
        }
    }

    public static class Extensions
    {
        public static ExpandoObject ToExpando(this IDictionary<string, object> dictionary)
        {
            var expando = new ExpandoObject();
            var expandoDic = (IDictionary<string, object>)expando;

            // go through the items in the dictionary and copy over the key value pairs)
            foreach (var kvp in dictionary)
            {
                // if the value can also be turned into an ExpandoObject, then do it!
                if (kvp.Value is IDictionary<string, object>)
                {
                    var expandoValue = ((IDictionary<string, object>)kvp.Value).ToExpando();
                    expandoDic.Add(kvp.Key, expandoValue);
                }
                else if (kvp.Value is ICollection)
                {
                    // iterate through the collection and convert any strin-object dictionaries
                    // along the way into expando objects
                    var itemList = new List<object>();
                    foreach (var item in (ICollection)kvp.Value)
                    {
                        if (item is IDictionary<string, object>)
                        {
                            var expandoItem = ((IDictionary<string, object>)item).ToExpando();
                            itemList.Add(expandoItem);
                        }
                        else
                        {
                            itemList.Add(item);
                        }
                    }

                    expandoDic.Add(kvp.Key, itemList);
                }
                else
                {
                    expandoDic.Add(kvp);
                }
            }

            return expando;
        }

        public static Expression<Func<TSource, dynamic>> DynamicFields<TSource>(IEnumerable<string> fields, List<DynamicProperty> properties)
        {
            var source = Expression.Parameter(typeof(TSource), "o");

            var resultType = DynamicClassFactory.CreateType(properties, false);
            var bindings = properties.Select(p => Expression.Bind(resultType.GetProperty(p.Name), Expression.Property(source, p.Name)));
            var result = Expression.MemberInit(Expression.New(resultType), bindings);
            return Expression.Lambda<Func<TSource, dynamic>>(result, source);
        }

        public static IQueryable<T> CloneListAs<T>(IList<object> source)
        {
            // Here we can do anything we want with T
            // T == source[0].GetType()
            return source.Cast<T>().AsQueryable();
        }
    }

    public class Globals
    {
        public dynamic data;
    }

    [ExcludeFromCodeCoverage]
    [TestFixture]
    public class DynamicModelFilterTest
    {
        [Test]
        public void TestJsonModel()
        {
            var data = @"{
		        'Id': 999,
		        'ParentId': null,
		        'TypeName': 'PROCESS.ITEM.END',
		        'Name': 'END'
	        }";

            try
            {
                var rule2 = new FilterRule
                {
                    Condition = "and",
                    Field = "Id",
                    Id = "Id",
                    Input = "NA",
                    Operator = "equal",
                    Type = "integer",
                    Value = "999",
                };

                var expObjData = JsonConvert.DeserializeObject<ExpandoObject>(data);

                var propData = expObjData as IDictionary<string, Object>;

                var newObj = CreateDataObject(propData,out Type type);

                var ressss = GetFilterData(newObj, rule2);

                //var quarableList = new[] { new { TypeName = "PROCESS.ITEM.END" } }.ToList().AsQueryable();
                //var resr = listt.BuildQuery(rule2).ToList();

                var sonuc = ressss as List<dynamic>;
            }
            catch (Exception ex)
            {

            }
        }

        [Test]
        public void TestDynamicProperties()
        {
            var data = @"{
		        'Id': 999,
		        'ParentId': null,
		        'TypeName': 'PROCESS.ITEM.END',
		        'Name': 'END'
	        }";

            var expObj = JsonConvert.DeserializeObject<ExpandoObject>(data);

            var newDataObj = expObj as IDictionary<string, Object>;

            //var newObj = new ExpandoObject() as IDictionary<string, Object>;
            //newObj.Add("FirstName", "Masallah");
            //newObj.Add("LastName", "ÖZEN");
            //newObj.Add("Age", 36);

            //var expr = $"{newObj["Age"]} ";
            //newDataObj.Add("FirstName", "Masallah");

            dynamic newObj = newDataObj.ToExpando();

            //var scriptContent = @" ((System.Collections.Generic.IDictionary<System.String, System.Object>)data)[""ParentId""] == null ";
            var scriptContent = @" data.ParentId == null ";

            var refs = new List<MetadataReference>{
                MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException).GetTypeInfo().Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).GetTypeInfo().Assembly.Location)
            };

            var script = CSharpScript.Create(scriptContent, options: ScriptOptions.Default.AddReferences(refs), globalsType: typeof(Globals));

            script.Compile();

            // create new global that will contain the data we want to send into the script
            var g = new Globals() { data = newObj };

            //Execute and display result
            var r = script.RunAsync(g).Result;

            newDataObj.Add("FirstName", "Masallah");
            newDataObj.Add("HasParentId", r.ReturnValue);

            //var rule = new FilterRule
            //{
            //    Condition = "and",
            //    Field = "HasParentId",
            //    Id = "HasParentId",
            //    Input = "NA",
            //    Operator = "equal",
            //    Type = "boolean",
            //    Value = "true",
            //};

            var rule = new FilterRule
            {
                Condition = "and",
                Field = "FirstName",
                Id = "HasParentId",
                Input = "NA",
                Operator = "begins_with",
                Type = "string",
                Value = "M",
            };

            ExpandoObject genereatedObj = newDataObj.ToExpando();

            var createdObj = CreateDataObject(genereatedObj as IDictionary<string, Object>,out Type type);

            var ressss = GetFilterData(createdObj,rule);

        }
        
        [Test]
        public void TestBoundedReferenceDataActionItem()
        {
            var dataItem = @"{
		        'Id': 999,
		        'PersonImsId': 123,
		        'CreatorImsId': 123
	        }";

            var boundRefDataItem = new BoundedReferenceDataActionItem { 
                FormId = 1,
                Mode = 1,
                ComponentId = 12,
                StateId = 1
            };

            var expObj = JsonConvert.DeserializeObject<ExpandoObject>(dataItem);

            var newDataObj = expObj as IDictionary<string, Object>;

            newDataObj.Add("FirstName", "Masallah");
            newDataObj.Add("LastName", "ÖZEN");
            newDataObj.Add("Age", 36);

            dynamic newObj = newDataObj.ToExpando();

            boundRefDataItem.DataItem = newObj;

            string mergedDataItem = JsonConvert.SerializeObject(boundRefDataItem);

            var jsonObjectData = JObject.Parse(mergedDataItem);

            string line = JsonUtils.GenerateDynamicLinqStatement(jsonObjectData);

            var queryable = new[] { jsonObjectData }.AsQueryable<JObject>().Select(line);


            var rule = new FilterRule
            {
                Field = "DataItem.Age",
                Id = "FirstName",
                Input = "NA",
                Operator = "greater_or_equal",
                Type = "integer",
                Value = "36"
            };

            var filterRule = new FilterRule
            {
                Condition = "and",
                Rules = new List<FilterRule>()
                {
                    rule
                }
            };

            var res = queryable.BuildQuery(filterRule).Any();
        }

        [Test]
        public void TestBoundedReferenceDataActionItemDynamicProperties()
        {
            //   var dataItem = @"{
            // 'Id': 999,
            // 'PersonImsId': 123,
            // 'CreatorImsId': 123
            //}";

            var rule = new FilterRule
            {
                Condition = "and",
                Field = "DataItem.PersonImsId",
                Id = "DataOwner",
                Input = "NA",
                Operator = "equal",
                Type = "integer",
                Value = "DataItem.CreatorImsId"
            };

            var dataItem = @"{
		        'FormId': 1,
		        'Mode': 1,
                'DataItem':{
		            'Id': 999,
		            'PersonImsId': 123,
		            'CreatorImsId': 123
	            }
	        }";

            var jsonObjectData = JObject.Parse(dataItem);

            string line = JsonUtils.GenerateDynamicLinqStatement(jsonObjectData);

            var queryable = new[] { jsonObjectData }.AsQueryable<JObject>().Select(line);

            var asss = queryable.Where("DataItem.PersonImsId == DataItem.CreatorImsId ").ToDynamicList();

            bool result = queryable.Any("DataItem.PersonImsId == DataItem.CreatorImsId ");

            var filterRule = new FilterRule
            {
                Condition = "and",
                Rules = new List<FilterRule>()
                {
                    rule
                }
            };

            var aa = QueryBuilder.BuildQuery(queryable, filterRule).Any();

            dynamic _expObj = JsonConvert.DeserializeObject<ExpandoObject>(dataItem);

            var dataItemObj = CreateDataObject(_expObj as IDictionary<string, Object>, out Type type1);

            var fieldDictList = (_expObj as IDictionary<string, Object>);


            //var boundRefDataItem = new BoundedReferenceDataActionItem
            //{
            //    FormId = 1,
            //    Mode = 1,
            //    ComponentId = 12,
            //    StateId = 1
            //};

            //dynamic expObj = JsonConvert.DeserializeObject<ExpandoObject>(dataItem);

            //var _dataItemObj = CreateDataObject(expObj as IDictionary<string, Object>,out Type type);

            ////boundRefDataItem.DataItem = dataItemObj;
            //var boundRefDataItem = new
            //{
            //    FormId = 1,
            //    Mode = 1,
            //    ComponentId = 12,
            //    StateId = 1,
            //    DataItem = dataItemObj
            //};

            var res = new[] { dataItemObj }.AsQueryable().BuildQuery(rule).ToList();

        }

        private object CreateDataObject(IDictionary<string, Object> propData, out Type type)
        {

            #region Another trying
            //var fields = propData
            //   .Select(p => p.Key)
            //   .ToList();

            //var properties = propData
            //.Select(p => new DynamicProperty(p.Key, typeof(object)))
            //.ToList();

            //var resultType = DynamicClassFactory.CreateType(properties, false);

            //var definition = Activator.CreateInstance(resultType); 
            #endregion

            var fieldList = new List<dynamic>();

            foreach (var item in propData)
            {
                fieldList.Add(new { FieldName = item.Key, FieldType = item.Value != null ? item.Value.GetType() : typeof(object) });
            }

            var newObject = MyTypeBuilder.CreateNewObject(fieldList,out Type newType);

            foreach (var item in propData)
            {
                var value = propData[item.Key];

                if (value?.GetType().Name == "ExpandoObject")
                {
                    var newJson = JsonConvert.SerializeObject(value);
                    var newJsonExpando = JsonConvert.DeserializeObject<ExpandoObject>(newJson) as IDictionary<string, Object>;

                    var newChildObj = CreateDataObject(newJsonExpando, out Type subType);
                    newObject.GetType().GetProperty(item.Key).SetValue(newObject, newChildObj);
                }
                else
                {
                    newObject.GetType().GetProperty(item.Key).SetValue(newObject, value);
                }
            }
            type = newType;
            return newObject;
        }

        private object GetFilterData(object newObject, FilterRule rule)
        {
            IList<object> sourceList = new[] { newObject }.ToList();

            MethodInfo method = typeof(Extensions).GetMethod("CloneListAs");
            MethodInfo genericMethod = method.MakeGenericMethod(sourceList[0].GetType());

            var genericList = genericMethod.Invoke(null, new[] { sourceList });

            var response = typeof(QueryBuilder)
            .GetMethod("BuildDynamicQuery", BindingFlags.Static | BindingFlags.Public)
            .MakeGenericMethod(newObject.GetType())
            .Invoke(null, new object[] { genericList, rule });

            return response;
        }

    }
}
