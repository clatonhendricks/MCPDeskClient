using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using MCPClient.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCPClient.Converters;

public class RoleToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is MessageRole role)
        {
            return role switch
            {
                MessageRole.User => HorizontalAlignment.Right,
                MessageRole.Assistant => HorizontalAlignment.Left,
                MessageRole.Tool => HorizontalAlignment.Left,
                _ => HorizontalAlignment.Left
            };
        }
        return HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class RoleToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is MessageRole role)
        {
            return role switch
            {
                MessageRole.User => Colors.DodgerBlue,
                MessageRole.Assistant => Colors.DimGray,
                MessageRole.Tool => Colors.DarkSlateGray,
                MessageRole.System => Colors.DarkOrange,
                _ => Colors.Gray
            };
        }
        return Colors.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class ArgsToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is IEnumerable<string> args)
        {
            return string.Join(" ", args);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
