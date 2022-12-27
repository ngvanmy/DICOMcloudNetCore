using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace DICOMcloud.DataAccess.Database
{
    public class PostgresqlDatabaseFactory : IDatabaseFactory
    {
        public string ConnectionString { get; protected set; }

        public PostgresqlDatabaseFactory(IConnectionStringProvider connectionStringProvider)
        {
            ConnectionString = connectionStringProvider.ConnectionString;
        }

        public PostgresqlDatabaseFactory(string connectionString)
        {
            ConnectionString = connectionString;
        }
        public IDbCommand CreateCommand()
        {
            return new NpgsqlCommand();
        }

        public IDbConnection CreateConnection()
        {
            return new NpgsqlConnection(ConnectionString);
        }

        public IDbDataParameter CreateParameter(string parameterName, object value)
        {
            return new NpgsqlParameter(parameterName, value ?? DBNull.Value);
        }

       
    }
}
