namespace Sitecore.Support.XA.Foundation.Search.Providers.Azure
{
  using Sitecore;
  using Sitecore.ContentSearch;
  using Sitecore.ContentSearch.Azure;
  using Sitecore.ContentSearch.Azure.Converters;
  using Sitecore.ContentSearch.Azure.Models;
  using Sitecore.ContentSearch.Azure.Schema;
  using Sitecore.ContentSearch.Converters;
  using Sitecore.Diagnostics;
  using Sitecore.XA.Foundation.Search.Providers.Azure.Geospatial;
  using System;
  using System.Collections;
  using System.Collections.Generic;
  using System.ComponentModel;
  using System.Globalization;
  using System.Runtime.InteropServices;

  public class CloudIndexFieldStorageValueFormatter : Sitecore.ContentSearch.Azure.Converters.CloudIndexFieldStorageValueFormatter, ISearchIndexInitializable
  {
    private ICloudSearchIndex _searchIndex;

    public CloudIndexFieldStorageValueFormatter()
    {
      base.EnumerableConverter = new IndexFieldEnumerableConverter(this);
    }

    private object ConvertToType(object value, Type expectedType, ITypeDescriptorContext context)
    {
      object obj2;
      Type c = value.GetType();
      if (c == expectedType)
      {
        return value;
      }
      if (typeof(IEnumerable<string>).IsAssignableFrom(c) && typeof(IEnumerable<string>).IsAssignableFrom(expectedType))
      {
        return value;
      }
      TypeConverter converter = this.GetConverter(value.GetType());
      if ((converter != null) && converter.CanConvertTo(context, expectedType))
      {
        return converter.ConvertTo(context, CultureInfo.CurrentCulture, value, expectedType);
      }
      if (!this.TryConvertToPrimitiveType(value, expectedType, context, out obj2) && !this.TryConvertToEnumerable(value, expectedType, context, out obj2))
      {
        throw new InvalidCastException($"Cannon cast value '{value}' of type '{value.GetType()}' to '{expectedType}'.");
      }
      return obj2;
    }

    public override object FormatValueForIndexStorage(object value, string fieldName)
    {
      Assert.IsNotNullOrEmpty(fieldName, "fieldName");
      object obj2 = value;
      if (obj2 == null)
      {
        return null;
      }
      IndexedField fieldByCloudName = this._searchIndex.SchemaBuilder.GetSchema().GetFieldByCloudName(fieldName);
      if (fieldByCloudName == null)
      {
        return value;
      }
      ICloudSearchTypeMapper cloudTypeMapper = this._searchIndex.CloudConfiguration.CloudTypeMapper;
      Type expectedType = (fieldByCloudName.Type.ToLower(CultureInfo.InvariantCulture) == "edm.geographypoint") ? typeof(GeoPoint) : cloudTypeMapper.GetNativeType(fieldByCloudName.Type);
      IndexFieldConverterContext context = new IndexFieldConverterContext(fieldName);
      try
      {
        if (obj2 is IIndexableId)
        {
          obj2 = this.FormatValueForIndexStorage(((IIndexableId)obj2).Value, fieldName);
        }
        else if (obj2 is IIndexableUniqueId)
        {
          obj2 = this.FormatValueForIndexStorage(((IIndexableUniqueId)obj2).Value, fieldName);
        }
        else
        {
          obj2 = this.ConvertToType(obj2, expectedType, context);
        }
        if ((obj2 != null) && ((!(obj2 is string) && !expectedType.IsInstanceOfType(obj2)) && (!(obj2 is IEnumerable<string>) || !typeof(IEnumerable<string>).IsAssignableFrom(expectedType))))
        {
          throw new InvalidCastException($"Converted value has type '{obj2.GetType()}', but '{expectedType}' is expected.");
        }
      }
      catch (Exception exception)
      {
        throw new NotSupportedException($"Field '{fieldName}' with value '{value}' of type '{value.GetType()}' cannot be converted to type '{expectedType}' declared for the field in the schema.", exception);
      }
      return obj2;
    }

    private TypeConverter GetConverter(Type type)
    {
      TypeConverter typeConverter = base.Converters.GetTypeConverter(type);
      if (typeConverter == null)
      {
        Type[] interfaces = type.GetInterfaces();
        foreach (Type type2 in interfaces)
        {
          typeConverter = base.Converters.GetTypeConverter(type2);
          if (typeConverter != null)
          {
            return typeConverter;
          }
        }
      }
      return typeConverter;
    }

    public void Initialize(ISearchIndex searchIndex)
    {
      ICloudSearchIndex index = searchIndex as ICloudSearchIndex;
      if (index == null)
      {
        throw new NotSupportedException($"Only {typeof(CloudSearchProviderIndex).Name} is supported");
      }
      this._searchIndex = index;
      base.Initialize(this._searchIndex);
    }

    private bool TryConvertToEnumerable(object value, Type expectedType, ITypeDescriptorContext context, out object result)
    {
      if (typeof(IEnumerable<string>).IsAssignableFrom(expectedType))
      {
        if ((value is string) || !(value is IEnumerable))
        {
          object obj2 = this.ConvertToType(value, typeof(string), context);
          if (!(obj2 is string))
          {
            result = null;
            return false;
          }
          string[] textArray1 = new string[] { (string)obj2 };
          result = textArray1;
          return true;
        }
        if (!(value is IEnumerable<string>))
        {
          List<string> list = new List<string>();
          foreach (object obj3 in (IEnumerable)value)
          {
            object obj4 = this.ConvertToType(obj3, typeof(string), context);
            if (!(obj4 is string))
            {
              result = null;
              return false;
            }
            list.Add((string)obj4);
          }
          result = list.ToArray();
          return true;
        }
      }
      result = null;
      return false;
    }

    private bool TryConvertToPrimitiveType(object value, Type expectedType, ITypeDescriptorContext context, out object result)
    {
      if (((value as string) == string.Empty) && expectedType.IsValueType)
      {
        result = Activator.CreateInstance(expectedType);
        return true;
      }
      if (value is string)
      {
        string str = (string)value;
        if (expectedType == typeof(bool))
        {
          switch (str)
          {
            case "1":
              result = true;
              return true;

            case "0":
            case "":
              result = false;
              return true;
          }
        }
        else if (expectedType == typeof(DateTimeOffset))
        {
          if ((str.Length > 15) && (str[15] == ':'))
          {
            result = DateUtil.ParseDateTime(str, DateTime.MinValue);
          }
          else
          {
            result = DateTimeOffset.Parse(str);
          }
          return true;
        }
      }
      if ((value is IConvertible) && (((((expectedType == typeof(bool)) || (expectedType == typeof(string))) || ((expectedType == typeof(int)) || (expectedType == typeof(long)))) || (expectedType == typeof(double))) || (expectedType == typeof(float))))
      {
        result = System.Convert.ChangeType(value, expectedType);
        return true;
      }
      result = null;
      return false;
    }
  }
}
