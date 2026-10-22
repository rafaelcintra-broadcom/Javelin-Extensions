# Broadcom CA Test Data Manager (TDM) - Javelin Extension para Zowe CLI & z/OS Mainframe

## 1. Visão Geral
Esta extensão permite que o Broadcom TDM Javelin execute nativamente todas as operações de automação de Mainframe (z/OS) através do Zowe CLI e z/OSMF (z/OS Management Facility), com suporte completo a **Perfis Zowe v1/v2**, **Conexão Direta**, **API Tokens (Zowe API Mediation Layer - APIML / JWT / LTPA)** e **Certificados Digitais X.509**.

---

## 2. Como é Definida a Conexão e o Profile?

O Javelin oferece três formas flexíveis para definir as credenciais e o destino do mainframe em todas as atividades:

### Modo 1: Via Profile Pré-Configurado (Team Config v2 ou V1 Profiles)
Se o ambiente já possui um perfil configurado (`zowe.config.json` ou `~/.zowe/profiles`):
- **`ProfileName`**: Nome do perfil z/OSMF (ex: `zosmf_dev`, `lpar1`, `sysa`).
- **`BaseProfileName`**: Nome do base profile / APIML (ex: `base_gateway`).
> O Zowe CLI herdará automaticamente host, porta, certificados e autenticação já gravados no perfil.

### Modo 2: Via API Token (Zowe APIML / JWT / Bearer / LTPA) *(Recomendado para CI/CD e TDM)*
Para autenticações modernas sem trafegar usuário e senha em cada passo:
1. Use a atividade **`Zowe Authenticate (API ML Login)`** informando o usuário e senha do RACF/ACF2 para gerar o Token.
2. Nas atividades seguintes (Jobs, Datasets, TSO, etc.), preencha a propriedade:
   - **`ApiToken`**: Variável contendo o token retornado (ex: `myToken`).
   - **`TokenType`**: Tipo de token (ex: `apimlAuthenticationToken`, `Bearer`, `jwtToken`, `LtpaToken2` — padrão: `apimlAuthenticationToken`).
   - **`Host`**: `apiml-gateway.company.com`
   - **`Port`**: `10010`

### Modo 3: Conexão Direta (Credenciais Básicas)
- **`Host`**: Hostname ou IP do z/OSMF (ex: `zosmf.empresa.com.br`)
- **`Port`**: Porta HTTPS do z/OSMF (ex: `10443` ou `443`)
- **`Username`**: Usuário do mainframe (RACF / ACF2 / Top Secret)
- **`Password`**: Senha / Passphrase (campo com máscara `[JavelinPassword]`)
- **`RejectUnauthorized`**: `false` para aceitar certificados SSL auto-assinados.

### Modo 4: Autenticação por Certificado Digital X.509 (PKI)
- **`CertFilePath`**: Caminho do certificado cliente (`.pem` / `.cer`)
- **`CertKeyFilePath`**: Caminho da chave privada cliente (`.key`)

---

## 3. Atividades Disponíveis no Javelin

| Categoria | Atividade | Classe | Descrição |
|---|---|---|---|
| **Auth** | **Zowe Authenticate (API ML Login)** | `ZoweAuthLoginActivity` | Efetua login no APIML/Gateway e obtém o API Token para os demais passos. |
| **JES / Jobs** | **Zowe Submit Job** | `ZoweSubmitJobActivity` | Submete JCL (via dataset PDS, arquivo local ou texto inline), aguarda conclusão e retorna JobId e Return Code (CC 0000). |
| **JES / Jobs** | **Zowe Job Status** | `ZoweJobStatusActivity` | Consulta status (ACTIVE, OUTPUT) e return code de jobs, ou filtra lista por prefixo/owner. |
| **JES / Jobs** | **Zowe Download Spool** | `ZoweDownloadSpoolActivity` | Baixa sysout/spool (SYSPRINT, JESMSGLG, etc.) para texto e DataTable de linhas. |
| **Datasets** | **Zowe Dataset List** | `ZoweDatasetListActivity` | Lista Data Sets por padrão/máscara ou lista membros de um PDS. |
| **Datasets** | **Zowe Dataset Download** | `ZoweDatasetDownloadActivity` | Baixa arquivos sequenciais (PS) ou membros de PDS para variável ou arquivo local. |
| **Datasets** | **Zowe Dataset Upload** | `ZoweDatasetUploadActivity` | Envia arquivo local ou texto direto para Data Set ou membro no mainframe. |
| **Datasets** | **Zowe Dataset Allocate** | `ZoweDatasetAllocateActivity` | Cria Data Sets (PS, PDS, PDSE, Like) ou deleta Data Sets (`--for-sure`). |
| **Comandos** | **Zowe TSO Command** | `ZoweTsoCommandActivity` | Executa comandos TSO/E interativos ou scripts REXX. |
| **Comandos** | **Zowe USS Command** | `ZoweUssCommandActivity` | Executa comandos de shell Unix System Services (z/OS UNIX). |
| **Comandos** | **Zowe Console Command** | `ZoweConsoleCommandActivity` | Emite comandos de console de operador MVS/JES2 (ex: `D A,L`, `D IPLINFO`). |
| **Subsistemas** | **Zowe CICS Command** | `ZoweCicsCommandActivity` | Gerencia programas e transações CICS (`NewCopy`, `PhaseIn`, listagens). |
| **Subsistemas** | **Zowe DB2 Query** | `ZoweDb2QueryActivity` | Executa queries SQL diretamente no IBM DB2 for z/OS. |
| **Genérico** | **Zowe Raw CLI Command** | `ZoweCliCommandActivity` | Executa qualquer comando zowe CLI arbitrário com suporte a JSON e DataTable. |

---

## 4. Instruções de Instalação no Javelin
1. Copie o arquivo `GridTools.Javelin.ZoweActivities.dll` para a pasta de instalação do Javelin:
   `C:\Program Files (x86)\Grid-Tools\Javelin\` (ou pasta `Extensions`).
2. Reinicie o Javelin.
3. As atividades estarão disponíveis na Toolbox do Javelin sob a categoria `GridTools.Javelin.ZoweActivities`.
