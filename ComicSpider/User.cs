﻿namespace ComicSpider
{
	public partial class User
	{
		public static void CheckAndFix()
		{
			UserTableAdapters.Key_valueTableAdapter da = new UserTableAdapters.Key_valueTableAdapter();

			da.Adapter.SelectCommand = da.Connection.CreateCommand();
			da.Adapter.SelectCommand.CommandText =
@"PRAGMA foreign_keys = OFF;

create table if not exists [Key_value] (
	[Key] nvarchar(300) PRIMARY KEY NOT NULL,
	[Value] image
);
create table if not exists [Error_log] (
	[Date_time] datetime NOT NULL,
	[Url] nvarchar(300) PRIMARY KEY NOT NULL,
	[Title] nvarchar(300) NOT NULL,
	[Detail] text
);
create table if not exists [Volume_list] (
	[Url] nvarchar(300) PRIMARY KEY NOT NULL,
	[Name] nvarchar(300),
	[Index] int,
	[State] nvarchar(50),
	[Parent_url] nvarchar(300),
	[Parent_name] nvarchar(300),
	[Parent_cookie] text,
	[Path] nvarchar(300),
	[Data_time] datetime NOT NULL
);
create table if not exists [Page_list] (
	[Url] nvarchar(300) PRIMARY KEY NOT NULL,
	[Name] nvarchar(300),
	[Index] int,
	[State] nvarchar(50),
	[Size] float,
	[Parent_url] nvarchar(300),
	[Parent_name] nvarchar(300),
	[Parent_cookie] text,
	[Path] nvarchar(300),
	[Data_time] datetime NOT NULL
);";
			da.Connection.Open();
			da.Adapter.SelectCommand.ExecuteNonQuery();
			da.Connection.Close();
		}
	}
}
