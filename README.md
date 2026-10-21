# MongoDB Custom Extension for CA TDM Javelin

Custom activities library for **Broadcom / CA Test Data Manager (TDM) Javelin** to connect, query, insert, update, delete, aggregate, and run administrative commands on **MongoDB**.

## Activities Included

| Activity | Description | Key Inputs | Key Outputs |
|---|---|---|---|
| `MongoDB Find` (`MongoFindActivity`) | Query documents with filter, sort, projection, limit, and skip | `ConnectionString`, `DatabaseName`, `CollectionName`, `FilterJson`, `ProjectionJson`, `SortJson`, `Limit`, `Skip` | `OutJson` (string), `OutDataTable` (DataTable), `OutCount` (long) |
| `MongoDB Insert` (`MongoInsertActivity`) | Insert single document, array of documents, or rows from a Javelin `DataTable` | `ConnectionString`, `DatabaseName`, `CollectionName`, `DocumentJson` or `InputDataTable` | `InsertedCount` (int), `InsertedIdsJson` (string) |
| `MongoDB Update` (`MongoUpdateActivity`) | Update documents matching a filter (with `$set`, etc.) and optional upsert/updateMany | `ConnectionString`, `DatabaseName`, `CollectionName`, `FilterJson`, `UpdateJson`, `IsUpdateMany`, `IsUpsert` | `MatchedCount` (long), `ModifiedCount` (long), `UpsertedId` (string) |
| `MongoDB Delete` (`MongoDeleteActivity`) | Delete single or multiple documents matching a filter | `ConnectionString`, `DatabaseName`, `CollectionName`, `FilterJson`, `IsDeleteMany` | `DeletedCount` (long) |
| `MongoDB Aggregate` (`MongoAggregateActivity`) | Execute aggregation pipelines (`$match`, `$group`, `$sort`, etc.) | `ConnectionString`, `DatabaseName`, `CollectionName`, `PipelineJson` | `OutJson` (string), `OutDataTable` (DataTable), `OutCount` (long) |
| `MongoDB Execute Command` (`MongoExecuteCommandActivity`) | Run administrative or raw commands (e.g., `ping`, `dropDatabase`, `stats`) | `ConnectionString`, `DatabaseName`, `CommandJson` | `ResponseJson` (string), `IsOk` (bool) |

---

## Build Instructions

1. Open `MongoDB-JavelinExtension.csproj` in Visual Studio or build using MSBuild:
   ```cmd
   nuget restore packages.config -PackagesDirectory ..\packages
   msbuild MongoDB-JavelinExtension.csproj /p:Configuration=Release /p:Platform=x64
   ```

2. The output folder `bin\Release\` (or `bin\Debug\`) will contain:
   - `GridTools.Javelin.MongoDbActivities.dll`
   - `MongoDB.Driver.dll`
   - `MongoDB.Driver.Core.dll`
   - `MongoDB.Bson.dll`
   - `DnsClient.dll`

---

## Installation into Javelin

### Method 1: Via Javelin GUI
1. Open **Javelin**.
2. Go to **File → Options → Extensions** (or **Import Extension**).
3. Click **Browse** and select `GridTools.Javelin.MongoDbActivities.dll`.
4. Check the activities to import and assign the category name `MongoDB Activities`.
5. Click **Import Selected**.

### Method 2: Manual Registration (`extensions.xml`)
1. Copy the output DLLs into the Javelin extensions folder:
   - `C:\ProgramData\CA\JavelinConfig\x64\Extensions\`
2. Edit `C:\ProgramData\CA\JavelinConfig\x64\Extensions\extensions.xml` and add the content from `extensions.xml`.
3. Restart Javelin. The activities will appear in the toolbox under **MongoDB Activities**.
