﻿using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ClickHouse.Ado.Impl.ATG.Insert;
using ClickHouse.Ado.Impl.Data;

namespace ClickHouse.Ado
{
    public class ClickHouseCommand : IDbCommand
    {
        private readonly ClickHouseConnection _clickHouseConnection;

        public ClickHouseCommand(ClickHouseConnection clickHouseConnection)
        {
            _clickHouseConnection = clickHouseConnection;
        }

        public ClickHouseCommand(ClickHouseConnection clickHouseConnection, string text) : this(clickHouseConnection)
        {
            CommandText = text;
        }

        public void Dispose()
        {
        }

        public void Prepare()
        {
            throw new NotSupportedException();
        }

        public void Cancel()
        {
            throw new NotSupportedException();
        }
        public ClickHouseParameter CreateParameter()
        {
            return new ClickHouseParameter();
        }
        IDbDataParameter IDbCommand.CreateParameter()
        {
            return CreateParameter();
        }
        
        private void Execute(bool readResponse)
        {
            var insertParser = new Impl.ATG.Insert.Parser(new Impl.ATG.Insert.Scanner(new MemoryStream(Encoding.UTF8.GetBytes(CommandText))));
            insertParser.Parse();
            if (insertParser.errors.count == 0)
            {
                var xText = new StringBuilder("INSERT INTO ");
                xText.Append(insertParser.tableName);
                xText.Append("(");
                insertParser.fieldList.Aggregate(xText, (builder, fld) => builder.Append(fld).Append(','));
                xText.Remove(xText.Length - 1, 1);
                xText.Append(")VALUES");

                _clickHouseConnection.Formatter.RunQuery(xText.ToString(), QueryProcessingStage.Complete, null, null, null, false);
                var schema = _clickHouseConnection.Formatter.ReadSchema();
                if (schema.Columns.Count != insertParser.valueList.Count())
                    throw new FormatException($"Value count mismatch. Server expected {schema.Columns.Count} and query contains {insertParser.valueList.Count()}.");
                for (var i = 0; i < insertParser.valueList.Count(); i++)
                {
                    var val = insertParser.valueList.ElementAt(i);
                    if (val.Item2 == Parser.ConstType.Parameter)
                        schema.Columns[i].Type.ValueFromParam(Parameters[val.Item1]);
                    else
                        schema.Columns[i].Type.ValueFromConst(val.Item1, val.Item2);
                }
                _clickHouseConnection.Formatter.SendBlocks(new[] {schema});
            }
            else
            {
                _clickHouseConnection.Formatter.RunQuery(SubstituteParameters(CommandText), QueryProcessingStage.Complete, null, null, null, false);
            }
            if (!readResponse) return;
            _clickHouseConnection.Formatter.ReadResponse();
        }

        private static readonly Regex ParamRegex=new Regex("[@:](?<n>[a-z_][a-z0-9_])",RegexOptions.Compiled|RegexOptions.IgnoreCase);
        private string SubstituteParameters(string commandText)
        {
            return ParamRegex.Replace(commandText, m => Parameters[m.Groups["n"].Value].AsSubstitute());
        }

        public int ExecuteNonQuery()
        {
            Execute(true);
            return 0;
        }
        public IDataReader ExecuteReader()
        {
            return ExecuteReader(CommandBehavior.Default);
        }

        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            if((behavior &(CommandBehavior.SchemaOnly|CommandBehavior.KeyInfo|CommandBehavior.SingleResult|CommandBehavior.SingleRow|CommandBehavior.SequentialAccess))!=0)
                throw new NotSupportedException($"CommandBehavior {behavior} is not supported.");
            Execute(false);

            return new ClickHouseDataReader(_clickHouseConnection, behavior);
        }

        public object ExecuteScalar()
        {
            using (var reader = ExecuteReader())
            {
                if (!reader.Read()) return null;
                return reader.GetValue(0);
            }
        }

        IDbConnection IDbCommand.Connection { get; set; }
        public ClickHouseConnection Connection => _clickHouseConnection;
        public IDbTransaction Transaction { get; set; }
        public string CommandText { get; set; }
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        IDataParameterCollection IDbCommand.Parameters => Parameters;
        public ClickHouseParameterCollection Parameters { get; }=new ClickHouseParameterCollection();
        public UpdateRowSource UpdatedRowSource { get; set; }
    }
}