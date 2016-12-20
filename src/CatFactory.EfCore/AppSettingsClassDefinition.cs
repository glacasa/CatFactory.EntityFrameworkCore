﻿using CatFactory.DotNetCore;
using CatFactory.OOP;

namespace CatFactory.EfCore
{
    public class AppSettingsClassDefinition : CSharpClassDefinition
    {
        public AppSettingsClassDefinition()
        {
            Namespaces.Add("System");

            Name = "AppSettings";

            Properties.Add(new PropertyDefinition("String", "ConnectionString"));
        }
    }
}