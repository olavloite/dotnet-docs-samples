﻿/*
 * Copyright (c) 2019 Google LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy of
 * the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations under
 * the License.
 */

using CloudSql.Settings;
using Google.Cloud.Diagnostics.AspNetCore;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Polly;
using System;
using System.Data;
using System.Data.Common;
using System.IO;

namespace CloudSql
{
    public class Program
    {
        public static AppSettings AppSettings { get; private set; }

        public static void Main(string[] args)
        {
            BuildWebHost(args).Build().Run();
            // Create Database table if it does not exist.
            var connectionString = new NpgsqlConnection().GetPostgreSqlConnectionString();
            DbConnection connection = new NpgsqlConnection(connectionString.ConnectionString);
            connection.InitializeDatabase();
        }

        public static IWebHostBuilder BuildWebHost(string[] args)
        {
            ReadAppSettings();

            return WebHost.CreateDefaultBuilder(args)
                .UseGoogleDiagnostics(AppSettings.GoogleCloudSettings.ProjectId,
                        AppSettings.GoogleCloudSettings.ServiceName,
                        AppSettings.GoogleCloudSettings.Version)
                .UseStartup<Startup>()
                .UsePortEnvironmentVariable();
        }

        /// <summary>
        /// Read application settings from appsettings.json. 
        /// </summary>
        private static void ReadAppSettings()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // Read json config into AppSettings.
            AppSettings = new AppSettings();
            config.Bind(AppSettings);
        }
    }

    static class ProgramExtensions
    {
        public static DbConnection OpenWithRetry(this DbConnection connection)
        {
            // [START cloud_sql_postgres_dotnet_ado_backoff]
            connection = Policy
                .HandleResult<DbConnection>(conn => conn.State != ConnectionState.Open)
                .WaitAndRetry(new[]
                {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5)
                }, (result, timeSpan, retryCount, context) =>
                {
                    // Log any warnings here.
                })
                .Execute(() =>
                {
                    //Open connection.
                    try {
                        connection.Open();
                    }
                    catch (NpgsqlException e)
                    {
                         Console.WriteLine(
                             $"Error connecting to database: {e.Message}");
                    }
                    return connection;
                });
            // [END cloud_sql_postgres_dotnet_ado_backoff]
            return connection;
        }

        public static void InitializeDatabase(this DbConnection connection)
        {
            using(connection.OpenWithRetry())
            {
                using (var createTableCommand = connection.CreateCommand())
                {
                    createTableCommand.CommandText = @"
                        CREATE TABLE IF NOT EXISTS
                        votes(
                            vote_id SERIAL NOT NULL,
                            time_cast timestamp NOT NULL,
                            candidate VARCHAR(6) NOT NULL,
                            PRIMARY KEY (vote_id)
                        )";
                    createTableCommand.ExecuteNonQuery();
                }
            }      
        }

        public static NpgsqlConnectionStringBuilder GetPostgreSqlConnectionString(this DbConnection connection)
        {
            NpgsqlConnectionStringBuilder connectionString; 
            if (Environment.GetEnvironmentVariable("DB_HOST") != null)
            {
                connectionString = NewPostgreSqlTCPConnectionString();
            }
            else
            {
                connectionString = NewPostgreSqlUnixSocketConnectionString();
            }
            // [START cloud_sql_postgres_dotnet_ado_limit]
            // MaxPoolSize sets maximum number of connections allowed in the pool.
            connectionString.MaxPoolSize = 5;
            // MinPoolSize sets the minimum number of connections in the pool.
            connectionString.MinPoolSize = 0;
            // [END cloud_sql_postgres_dotnet_ado_limit]
            // [START cloud_sql_postgres_dotnet_ado_timeout]
            // Timeout sets the time to wait (in seconds) while
            // trying to establish a connection before terminating the attempt.
            connectionString.Timeout = 15;
            // [END cloud_sql_postgres_dotnet_ado_timeout]
            // [START cloud_sql_postgres_dotnet_ado_lifetime]
            // ConnectionIdleLifetime sets the time (in seconds) to wait before
            // closing idle connections in the pool if the count of all
            // connections exceeds MinPoolSize.
            connectionString.ConnectionIdleLifetime = 300;
            // [END cloud_sql_postgres_dotnet_ado_lifetime]
            return connectionString;
        }

        public static NpgsqlConnectionStringBuilder NewPostgreSqlTCPConnectionString()
        {
            // [START cloud_sql_postgres_dotnet_ado_connection_tcp]
            // Equivalent connection string:
            // "Uid=<DB_USER>;Pwd=<DB_PASS>;Host=<DB_HOST>;Database=<DB_NAME>;"
            var connectionString = new NpgsqlConnectionStringBuilder()
            {
                // The Cloud SQL proxy provides encryption between the proxy and instance.
                SslMode = SslMode.Disable,

                // Remember - storing secrets in plaintext is potentially unsafe. Consider using
                // something like https://cloud.google.com/secret-manager/docs/overview to help keep
                // secrets secret.
                Host = Environment.GetEnvironmentVariable("DB_HOST"),     // e.g. '127.0.0.1'
                // Set Host to 'cloudsql' when deploying to App Engine Flexible environment
                Username = Environment.GetEnvironmentVariable("DB_USER"), // e.g. 'my-db-user'
                Password = Environment.GetEnvironmentVariable("DB_PASS"), // e.g. 'my-db-password'
                Database = Environment.GetEnvironmentVariable("DB_NAME"), // e.g. 'my-database'
            };
            connectionString.Pooling = true;
            // Specify additional properties here.
            return connectionString;
            // [END cloud_sql_postgres_dotnet_ado_connection_tcp]
        }

        public static NpgsqlConnectionStringBuilder NewPostgreSqlUnixSocketConnectionString()
        {
            // [START cloud_sql_postgres_dotnet_ado_connection_socket]
            // Equivalent connection string:
            // "Server=<dbSocketDir>/<INSTANCE_CONNECTION_NAME>;Uid=<DB_USER>;Pwd=<DB_PASS>;Database=<DB_NAME>"
            String dbSocketDir = Environment.GetEnvironmentVariable("DB_SOCKET_PATH") ?? "/cloudsql";
            String instanceConnectionName = Environment.GetEnvironmentVariable("INSTANCE_CONNECTION_NAME");
            var connectionString = new NpgsqlConnectionStringBuilder()
            {
                // The Cloud SQL proxy provides encryption between the proxy and instance.
                SslMode = SslMode.Disable,
                // Remember - storing secrets in plaintext is potentially unsafe. Consider using
                // something like https://cloud.google.com/secret-manager/docs/overview to help keep
                // secrets secret.
                Host = String.Format("{0}/{1}", dbSocketDir, instanceConnectionName),
                Username = Environment.GetEnvironmentVariable("DB_USER"), // e.g. 'my-db-user
                Password = Environment.GetEnvironmentVariable("DB_PASS"), // e.g. 'my-db-password'
                Database = Environment.GetEnvironmentVariable("DB_NAME"), // e.g. 'my-database'
            };
            connectionString.Pooling = true;
            // Specify additional properties here.
            return connectionString;
            // [END cloud_sql_postgres_dotnet_ado_connection_socket]
        }

        // Google Cloud Run sets the PORT environment variable to tell this
        // process which port to listen to.
        public static IWebHostBuilder UsePortEnvironmentVariable(
            this IWebHostBuilder builder)
        {
            string port = Environment.GetEnvironmentVariable("PORT");
            if (!string.IsNullOrEmpty(port))
            {
                builder.UseUrls($"http://0.0.0.0:{port}");
            }
            return builder;
        }
    }
}
