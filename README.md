# Broadcom Javelin Extensions Suite

Enterprise Custom Activities for **Broadcom Test Data Manager (TDM)** & **Javelin**, enabling zero-leakage in-flight data masking migration, automatic Data Assistant tag template enforcement, encrypted memory-first secure vaults, and automated NoSQL Data Generator publishing across non-relational and modern data platforms.

---

## 📦 Extensions Included in this Suite

| Database / Data Platform | Javelin Activity | Binary DLL / Package |
|---|---|---|
| **MongoDB / Cosmos DB / DocumentDB** | `MongoProdToNonProdPipelineActivity` | `dist/MongoDB Javelin Extension.zip` |
| **Apache Cassandra / ScyllaDB** | `CassandraProdToNonProdPipelineActivity` | `dist/Cassandra Javelin Extension.zip` |
| **Google Cloud BigQuery** | `BigQueryProdToNonProdPipelineActivity` | `dist/BigQuery Javelin Extension.zip` |
| **Amazon DynamoDB / AWS KV Stores** | `DynamoDbProdToNonProdPipelineActivity` | `dist/DynamoDb Javelin Extension.zip` |
| **NoSQL Files (JSON / NDJSON / Parquet)** | `NoSqlFileProdToNonProdPipelineActivity` | `dist/NoSqlFile Javelin Extension.zip` |
| **Apache HBase / Hadoop Big Data** | `HBaseProdToNonProdPipelineActivity` | `dist/HBASE Javelin Extension.zip` |
| **Mainframe / Broadcom Zowe** | `ZoweActivities` | `dist/Zowe Javelin Extension.zip` |

---

## 🔒 4-Barrier In-Flight Security Architecture

Every activity adheres strictly to zero-leakage security:

1. **Restricted NTFS ACL Isolation**: Disables inheritance and grants exclusive Full Control solely to the executing process identity.
2. **OS / Windows EFS Encryption**: Encrypts the transient vault directory (`cipher /e` + AES-256).
3. **Primary RAM Buffering**: Sets `FILE_ATTRIBUTE_TEMPORARY` so sensitive records reside in physical memory without premature disk flushes.
4. **Forensic Zero-Fill Shredding**: Byte-by-byte overwriting with `0x00` and hardware `fsync` before deletion.

---

## ⚡ Core Features

- **TDM Connection Profile Auto-Resolution**: Resolves profiles by name (e.g. `MONGO_PROD_CLUSTER`) via TDM Portal REST API with AES / GT-Crypt password decryption.
- **Fail-Closed Tag Template Enforcement**: Queries `/TDMGeneratorService/api/ca/v1/data-assistant/templates` and aborts immediately if required masking rules are missing.
- **Official MetaFunctions Parity**: Allocates a memory-based ContextGenerator session (`/TestDataManager/api/ca/v1/tdod/contextGenerators`) for native Data Painter evaluation.
- **TDM NoSQL Generator Publishing**:
  - `Formulas`: Generates autonomous Data Painter expressions (`@seedlist(...)@`, `@dob(...)@`, `@randguid()@`, `@randhex(12,12)@`).
  - `ReplayMaskedData`: Persists exact masked values for rapid environment reset.
- **Ready-to-Use Verification Queries**: Outputs formatted shell and SQL queries in logs for instant QA auditing in target environments.

---

## 📖 Interactive Documentation

Open `Broadcom_TDM_NoSQL_Extensions_Suite_Guide.html` in any web browser for the full bilingual (PT-BR / EN) visual guide and interactive parameter reference.

---

## 🚀 Installation into Javelin Studio

1. Unzip the desired extension ZIP bundle from `dist/`.
2. Copy all `.dll` files to Javelin's extensions folder:
   ```text
   C:\Program Files (x86)\Grid-Tools\Javelin\Extensions\
   ```
3. Restart **Javelin Studio**. The activities will appear in your Toolbox under their respective categories.

---

### License
Copyright © 2026 Broadcom Inc. All Rights Reserved.
