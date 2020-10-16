using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Text;

namespace Castle.DynamicLinqQueryBuilder
{
    public class ToStringQueryFiltering
    {
        public IQueryable ApplyFiltering(IQueryable query, IFilterRule filter)
        {
            var paramList = new Dictionary<int, object>();
            int counter = -1;

            string filtering = GetFiltering(filter, ref paramList,ref counter);

            if (paramList.Where(x=>x.Value != null).Count() < 1)
            {
                return filtering != null ? query.Where(filtering) : query;
            }
            else
            {
                return filtering != null ? query.Where(filtering, paramList.OrderBy(o => o.Key).Select(s => s.Value).ToArray()) : query;
            }
        }

        private string GetFiltering(IFilterRule filter, ref Dictionary<int, object> paramObjList, ref int counter)
        {
            var finalExpression = string.Empty;

            foreach (var filterObject in filter.Rules)
            {
                if (filterObject.Rules?.Count()>1)
                {
                    var innerExpression = GetFiltering(filterObject, ref paramObjList,ref counter);

                    if (finalExpression.Length>0)
                    {
                        finalExpression += $" {filter.Condition} ({innerExpression}) ";
                    }
                    else
                    {
                        finalExpression += $" ({innerExpression}) ";
                    }
                }
                else
                {
                    var currentFilterObject = filterObject;

                    if (filterObject.Rules?.Count() == 1)
                    {
                        currentFilterObject = filterObject.Rules.First();                        
                    }

                    var expression = GetFilteringExpression(currentFilterObject, ref paramObjList, ref counter);

                    if (filter.Rules?.Count() > 1)
                    {
                        if (finalExpression.Length > 0)
                        {
                            finalExpression += $" {filter.Condition} ({expression}) ";
                        }
                        else
                        {
                            finalExpression += $" ({expression}) ";
                        }
                    }
                    else
                    {
                        finalExpression += expression;
                    }

                }
                
            }

            return finalExpression.Length == 0 ? "true" : finalExpression;
        }

        private string GetFilteringExpression(IFilterRule filterObject, ref Dictionary<int, object> paramObjList,ref int counter)
        {
            if (filterObject.Rules?.Count()>0)
            {
                return GetFiltering(filterObject, ref paramObjList, ref counter);
            }

            var finalExpression = string.Empty;

            counter++;

            var expression = GetExpression(filterObject.Type, filterObject.Field, filterObject.Operator, filterObject.Value.ToString(), counter);
            finalExpression += expression.Item1;

            paramObjList.Add(paramObjList.Count() + 1, expression.Item2);

            return finalExpression.Length == 0 ? "true" : finalExpression;
        }

        private Tuple<string, object> GetExpression(string dataType, string field, string op, string param, int counter)
        {
            object paramObj = null;

            string caseMod = string.Empty;
            string nullCheck = string.Empty;

            if (dataType == "string")
            {
                param = @"""" + param.ToUpper().ToLower(new System.Globalization.CultureInfo("tr-TR")) + @"""";
                if (op != "in")
                {                    
                    caseMod = ".ToLower()"; // always ignore case
                }
                
                nullCheck = $"{field} != null && ";
            }

            if (dataType == "datetime")
            {
                int i = param.IndexOf("GMT", StringComparison.Ordinal);
                if (i > 0)
                {
                    param = param.Remove(i);
                }
                var date = DateTime.Parse(param, new CultureInfo("en-US"));

                var str = $"DateTime({date.Year}, {date.Month}, {date.Day})";
                param = str;
            }

            if (dataType == "datetimeoffset")
            {
                int i = param.IndexOf("GMT", StringComparison.Ordinal);
                if (i > 0)
                {
                    param = param.Remove(i);
                }

                var date = DateTimeOffset.Parse(param, new CultureInfo("en-US"));

                paramObj = date;

                param = $"@{counter}";

            }

            string exStr;

            switch (op)
            {
                case "equal":
                    exStr = string.Format("({3}{0}{2} == {1})", field, param, caseMod, nullCheck);
                    break;

                case "not_equal":
                    exStr = string.Format("({3}{0}{2} != {1})", field, param, caseMod, nullCheck);
                    break;

                case "in":

                    if (dataType == "integer")
                    {
                        if (op == "in")
                        {
                            var valueItems = param.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);

                            if (valueItems.Length > 1)
                            {
                                exStr = "";
                                for (int i = 0; i < valueItems.Length; i++)
                                {
                                    if (i < valueItems.Length - 1)
                                    {
                                        exStr += string.Format("{0}{2} == {1} || ", field, valueItems[i], caseMod);
                                    }
                                    else if (i == valueItems.Length - 1)
                                    {
                                        exStr += string.Format("{0}{2} == {1} ", field, valueItems[i], caseMod);
                                    }
                                }
                                exStr = $" ( {field} != null && ({exStr}) )";
                            }
                            else
                            {
                                exStr = string.Format("({3}{0}{2} == {1})", field, param, caseMod, nullCheck);
                            }

                        }
                        else
                        {
                            exStr = string.Format("({3}{0}{2} == {1})", field, param, caseMod, nullCheck);
                        }
                    }
                    else
                    {
                        exStr = string.Format("({3}{0}{2}.Contains({1}))", field, param, caseMod, nullCheck);
                    }
                    break;

                case "not_in":
                    exStr = string.Format("({3}!{0}{2}.Contains({1}))", field, param, caseMod, nullCheck);
                    break;

                case "begins_with":
                    exStr = string.Format("({3}{0}{2}.StartsWith({1}))", field, param, caseMod, nullCheck);
                    break;

                case "not_begins_with":
                    exStr = string.Format("({3}{0}{2}.StartsWith({1})) == false", field, param, caseMod, nullCheck);
                    break;

                case "ends_with":
                    exStr = string.Format("({3}{0}{2}.EndsWith({1}))", field, param, caseMod, nullCheck);
                    break;

                case "not_ends_with":
                    exStr = string.Format("({3}{0}{2}.EndsWith({1})) == false", field, param, caseMod, nullCheck);
                    break;

                case "greater_or_equal":
                    exStr = string.Format("({3}{0}{2} >= {1})", field, param, caseMod, nullCheck);
                    break;

                case "greater":
                    exStr = string.Format("({3}{0}{2} > {1})", field, param, caseMod, nullCheck);
                    break;

                case "less_or_equal":
                    exStr = string.Format("({3}{0}{2} <= {1})", field, param, caseMod, nullCheck);
                    break;

                case "less":
                    exStr = string.Format("({3}{0}{2} < {1})", field, param, caseMod, nullCheck);
                    break;

                case "is_null":
                    exStr = string.Format("({3}{0}{2} == null)", field, param, caseMod, nullCheck);
                    break;

                case "is_not_null":
                    exStr = string.Format("({3}{0}{2} != null)", field, param, caseMod, nullCheck);
                    break;

                default:
                    exStr = string.Empty;
                    break;
            }

            return new Tuple<string, object>(exStr, paramObj);
        }

    }
}
