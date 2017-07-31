﻿using System;
using System.Collections.Generic;
using System.Linq;
using CatFactory.CodeFactory;
using CatFactory.Collections;
using CatFactory.DotNetCore;
using CatFactory.Mapping;
using CatFactory.OOP;
using CatFactory.SqlServer;

namespace CatFactory.EfCore
{
    public class RepositoryClassDefinition : CSharpClassDefinition
    {
        public RepositoryClassDefinition(ProjectFeature projectFeature)
        {
            ProjectFeature = projectFeature;

            Init();
        }

        public ProjectFeature ProjectFeature { get; set; }

        public override void Init()
        {
            Namespaces.Add("System");
            Namespaces.Add("System.Linq");
            Namespaces.Add("System.Threading.Tasks");
            Namespaces.Add("Microsoft.EntityFrameworkCore");

            foreach (var dbObject in ProjectFeature.DbObjects)
            {
                var table = ProjectFeature.Project.Database.Tables.FirstOrDefault(item => item.FullName == dbObject.FullName);

                if (table == null)
                {
                    continue;
                }

                if (table.HasDefaultSchema())
                {
                    Namespaces.AddUnique(ProjectFeature.Project.GetEntityLayerNamespace());
                }
                else
                {
                    Namespaces.AddUnique(ProjectFeature.GetProject().GetEntityLayerNamespace(table.Schema));
                }

                Namespaces.AddUnique(ProjectFeature.GetProject().GetDataLayerContractsNamespace());
            }

            Namespace = ProjectFeature.GetProject().GetDataLayerRepositoriesNamespace();

            Name = ProjectFeature.GetClassRepositoryName();

            BaseClass = "Repository";

            Implements.Add(ProjectFeature.GetInterfaceRepositoryName());

            Constructors.Add(new ClassConstructorDefinition(new ParameterDefinition(ProjectFeature.Project.Database.GetDbContextName(), "dbContext"))
            {
                ParentInvoke = "base(dbContext)"
            });

            var dbos = ProjectFeature.DbObjects.Select(dbo => dbo.FullName).ToList();
            var tables = ProjectFeature.Project.Database.Tables.Where(t => dbos.Contains(t.FullName)).ToList();

            foreach (var table in tables)
            {
                if (!Namespaces.Contains(ProjectFeature.GetProject().GetDataLayerDataContractsNamespace()))
                {
                    if (ProjectFeature.GetProject().Settings.EntitiesWithDataContracts.Contains(table.FullName) && !Namespaces.Contains(ProjectFeature.GetProject().GetDataLayerDataContractsNamespace()))
                    {
                        Namespaces.Add(ProjectFeature.GetProject().GetDataLayerDataContractsNamespace());
                    }
                }

                foreach (var fk in table.ForeignKeys)
                {
                    if (String.IsNullOrEmpty(fk.Child))
                    {
                        var child = ProjectFeature.Project.Database.FindTableBySchemaAndName(fk.Child);

                        if (child != null)
                        {
                            Namespaces.AddUnique(ProjectFeature.GetProject().GetDataLayerDataContractsNamespace());

                            //if (!Namespaces.Contains(String.Format("{0}.{1}", child.Schema, child.Name)))
                            //{
                                
                            //}
                        }
                    }
                }

                Methods.Add(GetGetAllMethod(ProjectFeature, table));

                AddGetByUniqueMethods(ProjectFeature, table);

                Methods.Add(GetGetMethod(ProjectFeature, table));
                Methods.Add(GetAddMethod(ProjectFeature, table));
                Methods.Add(GetUpdateMethod(ProjectFeature, table));
                Methods.Add(GetRemoveMethod(ProjectFeature, table));
            }
        }

        public MethodDefinition GetGetAllMethod(ProjectFeature projectFeature, IDbObject dbObject)
        {
            var returnType = String.Empty;

            var lines = new List<ILine>();

            var tableCast = dbObject as Table;

            if (tableCast == null)
            {
                returnType = dbObject.GetSingularName();

                lines.Add(new CodeLine("return query.Paging(pageSize, pageNumber);"));
            }
            else
            {
                if (ProjectFeature.GetProject().Settings.EntitiesWithDataContracts.Contains(tableCast.FullName))
                {
                    var entityAlias = CatFactory.NamingConvention.GetCamelCase(tableCast.GetEntityName());

                    returnType = tableCast.GetDataContractName();

                    var dataContractPropertiesSets = new[] { new { Source = String.Empty, Target = String.Empty } }.ToList();

                    foreach (var column in tableCast.Columns)
                    {
                        var propertyName = column.GetPropertyName();

                        dataContractPropertiesSets.Add(new { Source = String.Format("{0}.{1}", entityAlias, propertyName), Target = propertyName });
                    }

                    foreach (var foreignKey in tableCast.ForeignKeys)
                    {
                        var foreignTable = ProjectFeature.Project.Database.FindTableByFullName(foreignKey.References);

                        if (foreignTable == null)
                        {
                            continue;
                        }

                        

                        var foreignKeyAlias = CatFactory.NamingConvention.GetCamelCase(foreignTable.GetEntityName());

                        foreach (var column in foreignTable?.GetColumnsWithOutKey())
                        {
                            if (dataContractPropertiesSets.Where(item => item.Source == String.Format("{0}.{1}", entityAlias, column.GetPropertyName())).Count() == 0)
                            {
                                var source = String.Format("{0}.{1}", foreignKeyAlias, column.GetPropertyName());
                                var target = String.Format("{0}{1}", foreignTable.GetEntityName(), column.GetPropertyName());

                                dataContractPropertiesSets.Add(new { Source = source, Target = target });
                            }
                        }
                    }

                    lines.Add(new CodeLine("var query = from {0} in DbContext.Set<{1}>()", entityAlias, tableCast.GetEntityName()));

                    foreach (var foreignKey in tableCast.ForeignKeys)
                    {
                        var foreignTable = ProjectFeature.Project.Database.FindTableByFullName(foreignKey.References);

                        if (foreignTable == null)
                        {
                            continue;
                        }

                        var foreignKeyEntityName = foreignTable.GetEntityName();

                        var foreignKeyAlias = CatFactory.NamingConvention.GetCamelCase(foreignTable.GetEntityName());

                        Namespaces.AddUnique(projectFeature.GetProject().GetEntityLayerNamespace(foreignTable.Schema));

                        if (foreignKey.Key.Count == 0)
                        {
                            lines.Add(new CommentLine(1, " There isn't definition for key in foreign key '{0}' in your current database", foreignKey.References));
                        }
                        else if (foreignKey.Key.Count == 1)
                        {
                            if (foreignTable == null)
                            {
                                lines.Add(new CommentLine(1, " There isn't definition for '{0}' in your current database", foreignKey.References));
                            }
                            else
                            {
                                var column = tableCast.Columns.FirstOrDefault(item => item.Name == foreignKey.Key[0]);

                                var x = NamingConvention.GetPropertyName(foreignKey.Key[0]);
                                var y = NamingConvention.GetPropertyName(foreignTable.PrimaryKey.Key[0]);

                                if (column.Nullable)
                                {
                                    lines.Add(new CodeLine(1, "join {0}Join in DbContext.Set<{1}>() on {2}.{3} equals {0}Join.{4} into {0}Temp", foreignKeyAlias, foreignKeyEntityName, entityAlias, x, y));
                                    lines.Add(new CodeLine(2, "from {0} in {0}Temp.Where(relation => relation.{2} == {1}.{3}).DefaultIfEmpty()", foreignKeyAlias, entityAlias, x, y));
                                }
                                else
                                {
                                    lines.Add(new CodeLine(1, "join {0} in DbContext.Set<{1}>() on {2}.{3} equals {0}.{4}", foreignKeyAlias, foreignKeyEntityName, entityAlias, x, y));
                                }
                            }
                        }
                        else
                        {
                            // todo: add logic for foreign key with multiple key

                            lines.Add(new WarningLine(1, "// todo: add logic for foreignkey with multiple key"));
                        }
                    }

                    lines.Add(new CodeLine(1, "select new {0}", returnType));
                    lines.Add(new CodeLine(1, "{{"));

                    for (var i = 0; i < dataContractPropertiesSets.Count; i++)
                    {
                        var property = dataContractPropertiesSets[i];

                        if (String.IsNullOrEmpty(property.Source) && String.IsNullOrEmpty(property.Target))
                        {
                            continue;
                        }

                        lines.Add(new CodeLine(2, "{0} = {1}{2}", property.Target, property.Source, i < dataContractPropertiesSets.Count - 1 ? "," : String.Empty));
                    }

                    lines.Add(new CodeLine(1, "}};"));
                    lines.Add(new CodeLine());
                }
                else
                {
                    returnType = dbObject.GetSingularName();

                    lines.Add(new CodeLine("var query = DbContext.Set<{0}>().AsQueryable();", dbObject.GetSingularName()));
                    lines.Add(new CodeLine());

                }
            }

            var parameters = new List<ParameterDefinition>()
            {
                new ParameterDefinition("Int32", "pageSize", "10"),
                new ParameterDefinition("Int32", "pageNumber", "0")
            };

            if (tableCast != null)
            {
                if (tableCast.ForeignKeys.Count == 0)
                {
                    lines.Add(new CodeLine("return query.Paging(pageSize, pageNumber);"));
                }
                else
                {
                    var resolver = new ClrTypeResolver() as ITypeResolver;

                    for (var i = 0; i < tableCast.ForeignKeys.Count; i++)
                    {
                        var foreignKey = tableCast.ForeignKeys[i];

                        if (foreignKey.Key.Count == 1)
                        {
                            var column = tableCast.Columns.First(item => item.Name == foreignKey.Key[0]);

                            var parameterName = NamingConvention.GetParameterName(column.Name);

                            parameters.Add(new ParameterDefinition(resolver.Resolve(column.Type), parameterName, "null"));

                            if (column.IsString())
                            {
                                lines.Add(new CodeLine("if (!String.IsNullOrEmpty({0}))", NamingConvention.GetParameterName(column.Name)));
                                lines.Add(new CodeLine("{{"));
                                lines.Add(new CodeLine(1, "query = query.Where(item => item.{0} == {1});", column.GetPropertyName(), parameterName));
                                lines.Add(new CodeLine("}}"));
                                lines.Add(new CodeLine());
                            }
                            else
                            {
                                lines.Add(new CodeLine("if ({0}.HasValue)", NamingConvention.GetParameterName(column.Name)));
                                lines.Add(new CodeLine("{{"));
                                lines.Add(new CodeLine(1, "query = query.Where(item => item.{0} == {1});", column.GetPropertyName(), parameterName));
                                lines.Add(new CodeLine("}}"));
                                lines.Add(new CodeLine());
                            }
                        }
                        else
                        {
                            // todo: add logic for composed foreign key
                        }
                    }

                    lines.Add(new CodeLine("return query.Paging(pageSize, pageNumber);"));
                }
            }

            return new MethodDefinition(String.Format("IQueryable<{0}>", returnType), dbObject.GetGetAllMethodName(), parameters.ToArray())
            {
                Lines = lines
            };
        }

        public void AddGetByUniqueMethods(ProjectFeature projectFeature, IDbObject dbObject)
        {
            var table = dbObject as ITable;

            if (table == null)
            {
                table = projectFeature.Project.Database.FindTableBySchemaAndName(dbObject.FullName);
            }

            if (table == null)
            {
                return;
            }

            foreach (var unique in table.Uniques)
            {
                var expression = String.Format("item => {0}", String.Join(" && ", unique.Key.Select(item => String.Format("item.{0} == entity.{0}", NamingConvention.GetPropertyName(item)))));

                Methods.Add(new MethodDefinition(String.Format("Task<{0}>", dbObject.GetSingularName()), dbObject.GetGetByUniqueMethodName(unique, NamingConvention), new ParameterDefinition(dbObject.GetSingularName(), "entity"))
                {
                    IsAsync = true,
                    Lines = new List<ILine>()
                    {
                        new CodeLine("return await DbContext.{0}.FirstOrDefaultAsync({1});", ProjectFeature.GetProject().Settings.DeclareDbSetPropertiesInDbContext ? dbObject.GetPluralName() : String.Format("Set<{0}>()", dbObject.GetSingularName()), expression)
                    }
                });
            }
        }

        public MethodDefinition GetGetMethod(ProjectFeature projectFeature, IDbObject dbObject)
        {
            var table = projectFeature.Project.Database.FindTableBySchemaAndName(dbObject.FullName);

            var expression = String.Empty;

            if (table != null)
            {
                if (table.PrimaryKey == null)
                {
                    if (table.Identity != null)
                    {
                        expression = String.Format("item => item.{0} == entity.{0}", NamingConvention.GetPropertyName(table.Identity.Name));
                    }
                }
                else
                {
                    expression = String.Format("item => {0}", String.Join(" && ", table.PrimaryKey.Key.Select(item => String.Format("item.{0} == entity.{0}", NamingConvention.GetPropertyName(item)))));
                }
            }

            return new MethodDefinition(String.Format("Task<{0}>", dbObject.GetSingularName()), dbObject.GetGetMethodName(), new ParameterDefinition(dbObject.GetSingularName(), "entity"))
            {
                IsAsync = true,
                Lines = new List<ILine>()
                {
                    new CodeLine("return await DbContext.{0}.FirstOrDefaultAsync({1});", ProjectFeature.GetProject().Settings.DeclareDbSetPropertiesInDbContext ? dbObject.GetPluralName() : String.Format("Set<{0}>()", dbObject.GetSingularName()), expression)
                }
            };
        }

        public MethodDefinition GetAddMethod(ProjectFeature projectFeature, IDbObject dbObject)
        {
            var lines = new List<ILine>();

            var tableCast = dbObject as Table;

            if (tableCast != null)
            {
                if (tableCast.IsPrimaryKeyGuid())
                {
                    lines.Add(new CodeLine("entity.{0} = Guid.NewGuid();", NamingConvention.GetPropertyName(tableCast.PrimaryKey.Key[0])));
                    lines.Add(new CodeLine());
                }
            }

            lines.Add(new CodeLine("Add(entity);"));
            lines.Add(new CodeLine());
            lines.Add(new CodeLine("return await CommitChangesAsync();"));

            return new MethodDefinition("Task<Int32>", dbObject.GetAddMethodName(), new ParameterDefinition(dbObject.GetSingularName(), "entity"))
            {
                IsAsync = true,
                Lines = lines
            };
        }

        public MethodDefinition GetUpdateMethod(ProjectFeature projectFeature, IDbObject dbObject)
        {
            var lines = new List<ILine>();

            lines.Add(new CodeLine("Update(changes);"));
            lines.Add(new CodeLine());
            lines.Add(new CodeLine("return await CommitChangesAsync();"));

            return new MethodDefinition("Task<Int32>", dbObject.GetUpdateMethodName(), new ParameterDefinition(dbObject.GetSingularName(), "changes"))
            {
                IsAsync = true,
                Lines = lines
            };
        }

        public MethodDefinition GetRemoveMethod(ProjectFeature projectFeature, IDbObject dbObject)
        {
            return new MethodDefinition("Task<Int32>", dbObject.GetRemoveMethodName(), new ParameterDefinition(dbObject.GetSingularName(), "entity"))
            {
                IsAsync = true,
                Lines = new List<ILine>()
                {
                    new CodeLine("Remove(entity);"),
                    new CodeLine(),
                    new CodeLine("return await CommitChangesAsync();")
                }
            };
        }
    }
}
