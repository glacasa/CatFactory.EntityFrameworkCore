﻿using System.Linq;
using CatFactory.NetCore;
using CatFactory.NetCore.ObjectOrientedProgramming;
using CatFactory.ObjectOrientedProgramming;
using CatFactory.ObjectRelationalMapping;

namespace CatFactory.EntityFrameworkCore.Definitions.Extensions
{
    public static class DataContractClassBuilder
    {
        public static DataContractClassDefinition GetDataContractClassDefinition(this EntityFrameworkCoreProject project, ITable table, bool isDomainDrivenDesign)
        {
            var definition = new DataContractClassDefinition
            {
                Namespaces =
                {
                    "System"
                },
                Namespace = isDomainDrivenDesign ? project.GetDomainQueryModelsNamespace() : project.GetDataLayerDataContractsNamespace(),
                AccessModifier = AccessModifier.Public,
                Name = project.GetDataContractName(table),
                DbObject = table
            };

            foreach (var column in table.Columns)
            {
                definition.Properties.Add(new PropertyDefinition(AccessModifier.Public, project.Database.ResolveDatabaseType(column), project.GetPropertyName(table, column))
                {
                    IsAutomatic = true
                });
            }

            foreach (var foreignKey in table.ForeignKeys)
            {
                var foreignTable = project.Database.FindTable(foreignKey.References);

                if (foreignTable == null)
                    continue;

                var foreignKeyAlias = NamingConvention.GetCamelCase(project.GetEntityName(foreignTable));

                foreach (var column in foreignTable?.GetColumnsWithNoPrimaryKey())
                {
                    var col = (Column)column;

                    var propertyName = project.GetPropertyName(foreignTable, col);

                    var target = string.Format("{0}{1}", project.GetEntityName(foreignTable), propertyName);

                    if (definition.Properties.Count(item => item.Name == propertyName) == 0)
                        definition.Properties.Add(new PropertyDefinition(AccessModifier.Public, project.Database.ResolveDatabaseType(col), target)
                        {
                            IsAutomatic = true
                        });
                }
            }

            definition.SimplifyDataTypes();

            return definition;
        }
    }
}
