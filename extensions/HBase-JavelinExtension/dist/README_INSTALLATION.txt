# Quick Installation Guide — Broadcom CA TDM Javelin HBase Extension

## 1. Files Included in this Distribution (`dist/`)
Apenas 3 DLLs são necessárias agora:
- `GridTools.Javelin.HBaseActivities.dll` — Core Javelin Activity Library (WF4)
- `Renci.SshNet.dll` — SSH & Port Forwarding Engine (Versão 2020.0.1.0 — 100% nativa do CA TDM Javelin)
- `Newtonsoft.Json.dll` — JSON Parser & Serializer

Documentação e Exemplos:
- `Broadcom_TDM_Javelin_HBase_Extension_Guide.html` — Guia visual interativo com especificação de todas as propriedades e casos de teste.
- `examples/HBase_SSH_Examples.vwf` — Fluxo Javelin pronto demonstrando Put, Scan via SSH e carga direta em DataTable.
- `examples/HBase_Examples.vwf` — Fluxo Javelin pronto demonstrando execução via REST API / Stargate.

## 2. Instalação no Windows

### Passo A: Criar a pasta Extensions e Copiar as DLLs
1. Abra a pasta de instalação do Javelin (por exemplo, no drive `F:` ou `C:`):
   `F:\Program Files\Grid-Tools\Javelin\` (ou `C:\Program Files\Grid-Tools\Javelin\`)
2. Crie uma pasta chamada **`Extensions`** dentro dela:
   `F:\Program Files\Grid-Tools\Javelin\Extensions\`
3. Copie as 3 DLLs (`GridTools.Javelin.HBaseActivities.dll`, `Renci.SshNet.dll`, `Newtonsoft.Json.dll`) para dentro dessa pasta.

### Passo B: Desbloquear Binários no Windows (PowerShell como Administrador)
Execute para remover o bloqueio de segurança do Windows (caso as DLLs tenham vindo pela rede):
```powershell
Unblock-File "F:\Program Files\Grid-Tools\Javelin\Extensions\GridTools.Javelin.HBaseActivities.dll"
Unblock-File "F:\Program Files\Grid-Tools\Javelin\Extensions\Renci.SshNet.dll"
Unblock-File "F:\Program Files\Grid-Tools\Javelin\Extensions\Newtonsoft.Json.dll"
```

### Passo C: Importar no Javelin Designer
1. Abra o **CA TDM Javelin**.
2. Vá em **File** -> **Options** -> **Extensions** (ou botão **Import Extension** na barra superior).
3. Clique em **Browse...** / **Add** e selecione:
   `F:\Program Files\Grid-Tools\Javelin\Extensions\GridTools.Javelin.HBaseActivities.dll`
4. Defina o nome da categoria na toolbox como: **`HBase Activities`**.
5. Clique em **Import Selected**.

## 3. Modos Suportados
- **Modo SSH (Sem necessidade de REST Server)**:
  * `HBase SSH Shell Command`: Executa comandos arbitrários no `hbase shell`.
  * `HBase SSH Scan`: Faz varredura e popula diretamente um `DataTable` do Javelin.
  * `HBase SSH Put`: Insere registros a partir de JSON ou `DataTable`.
  * `HBase SSH Tunnel`: Abre túnel SSH em background caso precise acessar porta interna.
- **Modo REST / Stargate API**:
  * `HBase Get Row`, `HBase Put Row`, `HBase Scan`, `HBase Delete`, `HBase Table Admin`, `HBase Cluster Status`.
