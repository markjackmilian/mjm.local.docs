# mjm.local.docs — Documentazione di Riferimento

Guida tecnica completa: algoritmi, backend, provider, strumenti MCP e tutte le opzioni di configurazione.

---

## Indice

1. [Architettura e flusso dati](#1-architettura-e-flusso-dati)
2. [Vector Store — algoritmi e backend](#2-vector-store--algoritmi-e-backend)
3. [Embedding providers](#3-embedding-providers)
4. [Formati documento e chunking](#4-formati-documento-e-chunking)
5. [File storage](#5-file-storage)
6. [Chat — provider e modalità](#6-chat--provider-e-modalità)
7. [MCP Tools — riferimento completo](#7-mcp-tools--riferimento-completo)
8. [Domain model](#8-domain-model)
9. [Autenticazione](#9-autenticazione)
10. [Limiti e parametri fissi](#10-limiti-e-parametri-fissi)
11. [Configurazioni complete per scenario](#11-configurazioni-complete-per-scenario)

---

## 1. Architettura e flusso dati

### Layer

```
┌─────────────────────────────────────────────────────────────┐
│  Mjm.LocalDocs.Server  (Blazor UI + MCP endpoint)           │
│  - Pagine Blazor (Projects, Chat, Settings…)                │
│  - McpTools/ (un tool per operazione)                        │
│  - Program.cs (composizione e startup)                       │
└──────────────────────┬──────────────────────────────────────┘
                       │ dipende da
┌──────────────────────▼──────────────────────────────────────┐
│  Mjm.LocalDocs.Core  (dominio, zero dipendenze esterne)      │
│  - Abstractions/ (IVectorStore, IEmbeddingService, …)       │
│  - Models/ (Document, Project, DocumentChunk, …)            │
│  - Services/ (DocumentService, ApiTokenService)              │
└──────────────────────┬──────────────────────────────────────┘
                       │ implementato da
┌──────────────────────▼──────────────────────────────────────┐
│  Mjm.LocalDocs.Infrastructure  (implementazioni)            │
│  - VectorStore/  (InMemory, SQLite, HNSW, SqlServer)        │
│  - Embeddings/   (Fake, SemanticKernel → OpenAI/Azure/Ollama│
│  - Documents/    (PDF, Word, Markdown, PlainText readers)   │
│  - FileStorage/  (Database, FileSystem, AzureBlob)          │
│  - Persistence/  (EF Core, SQLite/SqlServer repositories)   │
└─────────────────────────────────────────────────────────────┘
```

### Flusso: upload documento

```
Utente carica file (UI o MCP add_document)
  │
  ▼
IDocumentReader.ExtractTextAsync()      ← estrae testo dal file
  │
  ▼
DocumentProcessor.ProcessAsync()        ← divide in chunk di MaxChunkSize caratteri
  │                                        con OverlapSize caratteri di sovrapposizione
  ▼
IEmbeddingService.GenerateEmbeddingsAsync()  ← genera vettori per ogni chunk
  │
  ├─► IDocumentRepository.AddAsync()    ← salva metadati + testo estratto (SQLite/SqlServer)
  ├─► IDocumentFileStorage.SaveFileAsync()   ← salva file originale (Database/FileSystem/Blob)
  └─► IVectorStore.UpsertBatchAsync()   ← indicizza i vettori (InMemory/Sqlite/HNSW/SqlServer)
```

### Flusso: ricerca semantica

```
Query testuale (UI, MCP search_docs, o Chat)
  │
  ▼
IEmbeddingService.GenerateEmbeddingAsync(query)   ← query → vettore
  │
  ▼
IVectorStore.SearchAsync(queryVector, limit)       ← ricerca nearest neighbor
  │  restituisce: [(chunkId, score), …]
  ▼
IDocumentRepository.GetChunksByIdsAsync()          ← recupera testo dei chunk
  │
  ▼
SearchResult[] con Score, Chunk.Content, Chunk.FileName, Chunk.DocumentId
```

---

## 2. Vector Store — algoritmi e backend

### Panoramica comparativa

| Provider | Config value | Algoritmo | Complessità ricerca | Persistenza | Caso d'uso |
|----------|-------------|-----------|--------------------|-----------|-|
| `InMemory` | `"InMemory"` | Brute-force cosine | O(n) | No — perso al riavvio | Dev / test |
| `Sqlite` | `"Sqlite"` | Brute-force cosine | O(n) | File SQLite | < ~10.000 chunk |
| `SqliteHnsw` | `"SqliteHnsw"` | HNSW (ANN) | O(log n) | SQLite + file `.bin` | 10k–500k chunk |
| `SqlServer` | `"SqlServer"` | DiskANN (ANN nativo) | O(log n) | SQL Server / Azure SQL | Enterprise, >100k chunk |

> **Chunk vs documenti**: un documento di 10.000 caratteri con `MaxChunkSize=3000` e `OverlapSize=300` produce circa 4 chunk. "10.000 chunk" corrisponde quindi a circa 2.500–3.000 documenti medi.

---

### 2.1 InMemory

**Classe**: `InMemoryVectorStore`
**Algoritmo**: cosine similarity brute-force in-memory
**Formula similarità**: `dot(a,b) / (|a| * |b|)` — punteggio 0–1 (1 = identici)

**Configurazione**:
```json
{
  "LocalDocs": {
    "Storage": { "Provider": "InMemory" }
  }
}
```

**Limitazioni**: i vettori si perdono a ogni riavvio. Non richiede connection string.

---

### 2.2 Sqlite (brute-force)

**Classe**: `SqliteVectorStore`
**Algoritmo**: cosine similarity brute-force su tutti i record
**Storage**: embedding come BLOB binario (`float[]` serializzato)
**Formula similarità**: stessa formula di InMemory — punteggio 0–1

**Schema DDL** (generato automaticamente al primo avvio):
```sql
CREATE TABLE IF NOT EXISTS chunk_embeddings (
    chunk_id TEXT PRIMARY KEY,
    embedding BLOB NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_chunk_embeddings_chunk_id ON chunk_embeddings(chunk_id);
```

**Configurazione**:
```json
{
  "ConnectionStrings": { "LocalDocs": "Data Source=localdocs.db" },
  "LocalDocs": {
    "Storage": { "Provider": "Sqlite" }
  }
}
```

---

### 2.3 SqliteHnsw (HNSW approssimato)

**Classe**: `HnswVectorStore`
**Algoritmo**: HNSW — *Hierarchical Navigable Small World graphs*
**Pubblicazione originale**: Malkov & Yashunin, 2018
**Storage**: grafo in memoria caricato da file `.bin` su disco

#### Come funziona HNSW

Il grafo è strutturato su livelli gerarchici. I livelli superiori sono sparsi (connessioni a lungo raggio), il livello 0 è denso (connessioni a corto raggio). La ricerca parte dall'alto e scende, restringendo i candidati a ogni livello fino al vicinato esatto nel livello 0.

- **Inserimento**: O(log n) — il nuovo nodo viene assegnato a un livello casuale con distribuzione esponenziale
- **Ricerca**: O(log n) — scansione top-down, lista candidati dinamica di dimensione `efSearch`

#### Parametri di configurazione

| Parametro | Default | Range consigliato | Effetto |
|-----------|---------|------------------|---------|
| `IndexPath` | `"hnsw_index.bin"` | — | Percorso file indice su disco |
| `MaxConnections` | `16` | `12–48` | Connessioni per nodo (M). Più alto → più recall, più RAM e tempo di build |
| `EfConstruction` | `200` | `100–500` | Qualità costruzione indice. Più alto → indice migliore, build più lenta |
| `EfSearch` | `50` | `50–500` | Qualità ricerca. Più alto → più recall, ricerca più lenta |
| `AutoSaveDelayMs` | `5000` | `0` (disabilita) – qualsiasi | Debounce auto-save su disco in ms. `0` = salvataggio manuale |

**Formula distanza → similarità**: `score = 1.0 - distance` (distanza cosine 0–1 convertita in similarità)

**Salvataggio**: scrittura atomica su file temporaneo poi rename. Salvataggio automatico al `Dispose()`.

**Configurazione**:
```json
{
  "ConnectionStrings": { "LocalDocs": "Data Source=localdocs.db" },
  "LocalDocs": {
    "Storage": {
      "Provider": "SqliteHnsw",
      "Hnsw": {
        "IndexPath": "hnsw_index.bin",
        "MaxConnections": 16,
        "EfConstruction": 200,
        "EfSearch": 50,
        "AutoSaveDelayMs": 5000
      }
    }
  }
}
```

> `ConnectionStrings.LocalDocs` è usata da SQLite per i metadati (documenti, chunk text). Il file `.bin` è separato e contiene solo il grafo HNSW.

---

### 2.4 SqlServer (DiskANN nativo)

**Classe**: `SqlServerVectorStore`
**Algoritmo**: DiskANN-based vector index nativo di SQL Server (quando `UseVectorIndex: true`)
**Tipo colonna**: `VECTOR(dimension)` — tipo nativo introdotto in SQL Server 2025

**Requisiti SQL Server**:
- SQL Server 2025+ (on-premise)
- Azure SQL Database (qualsiasi tier corrente)
- Azure SQL Managed Instance (Always-up-to-date)

**Schema DDL** (generato automaticamente al primo avvio):
```sql
CREATE TABLE [dbo].[chunk_embeddings] (
    chunk_id NVARCHAR(255) PRIMARY KEY,
    embedding VECTOR(1536) NOT NULL
);

-- Solo se UseVectorIndex: true
CREATE VECTOR INDEX vec_idx_chunk_embeddings
ON [dbo].[chunk_embeddings](embedding)
WITH (metric = 'cosine');
```

#### UseVectorIndex

| Valore | Tipo ricerca | Complessità | Latenza tipica | Quando usare |
|--------|-------------|-------------|----------------|-------------|
| `true` | ANN via DiskANN index | O(log n) | 10–50 ms | >10.000 vettori, priorità performance |
| `false` | Exact k-NN via `VECTOR_DISTANCE()` | O(n) | 100–500 ms | <50.000 vettori con requisiti di precisione assoluta |

Se SQL Server non supporta `CREATE VECTOR INDEX` (versione troppo vecchia), l'errore viene ignorato silenziosamente e il sistema degrada automaticamente a exact k-NN.

#### DistanceMetric

| Valore | Formula | Quando usare |
|--------|---------|-------------|
| `"cosine"` (default) | angolo tra vettori — ignora la magnitudine | Sempre per embeddings testuali |
| `"euclidean"` | distanza L2 geometrica | Quando la magnitudine è rilevante (raro con text embeddings) |
| `"dotproduct"` | prodotto scalare | Con vettori pre-normalizzati (alcune config Ollama) |

**Formula distanza → similarità**: `score = 1.0 / (1.0 + distance)`

> La metrica deve essere la stessa tra la creazione dell'indice e le query. Cambiare `DistanceMetric` dopo la creazione della tabella richiede di eliminare e ricreare l'indice.

**Configurazione**:
```json
{
  "ConnectionStrings": {
    "LocalDocs": "Server=myserver.database.windows.net;Database=localdocs;..."
  },
  "LocalDocs": {
    "Storage": {
      "Provider": "SqlServer",
      "SqlServer": {
        "Schema": "dbo",
        "TableName": "chunk_embeddings",
        "UseVectorIndex": true,
        "DistanceMetric": "cosine"
      }
    }
  }
}
```

---

## 3. Embedding providers

### Panoramica

| Provider | Config value | Classe | Modello default | Dimensione | Richiede API key |
|----------|-------------|--------|----------------|------------|-----------------|
| `Fake` | `"Fake"` | `FakeEmbeddingService` | — | 1536 (configurabile) | No |
| `OpenAI` | `"OpenAI"` | `SemanticKernelEmbeddingService` | `text-embedding-3-small` | 1536 | Sì |
| `AzureOpenAI` | `"AzureOpenAI"` | `SemanticKernelEmbeddingService` | deployment configurabile | 1536 | Sì |
| `Ollama` | `"Ollama"` | `SemanticKernelEmbeddingService` | `nomic-embed-text` | 768 | No (locale) |

> **Attenzione**: il valore `Embeddings:Dimension` deve corrispondere esattamente alla dimensione del modello scelto. Una mancata corrispondenza causa errori al momento del salvataggio/ricerca nel vector store.

---

### 3.1 Fake (sviluppo)

Genera vettori deterministici basati su hash delle parole e dei caratteri. Nessuna chiamata di rete. Adatto solo per sviluppo e test — non produce vettori semanticamente significativi.

**Algoritmo**:
1. Normalizza il testo in minuscolo
2. Divide in parole e hasha ciascuna → `embedding[hash % dimension] += 1.0`
3. Aggiunge feature a livello di carattere → `embedding[(char * 7) % dimension] += 0.1`
4. Normalizzazione L2 del vettore risultante

```json
{
  "LocalDocs": {
    "Embeddings": { "Provider": "Fake", "Dimension": 1536 }
  }
}
```

---

### 3.2 OpenAI

Variabili d'ambiente supportate (priorità su appsettings): `OPENAI_API_KEY`

**Modelli disponibili**:

| Modello | Dimensione | Note |
|--------|-----------|------|
| `text-embedding-3-small` (default) | 1536 | Rapido, economico, ottimo per la maggior parte dei casi |
| `text-embedding-3-large` | 3072 | Qualità superiore, più costoso — impostare `Dimension: 3072` |
| `text-embedding-ada-002` | 1536 | Legacy, mantenuto per compatibilità |

```json
{
  "LocalDocs": {
    "Embeddings": {
      "Provider": "OpenAI",
      "Dimension": 1536,
      "OpenAI": {
        "ApiKey": "sk-...",
        "Model": "text-embedding-3-small"
      }
    }
  }
}
```

---

### 3.3 AzureOpenAI

Variabili d'ambiente supportate: `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY`

```json
{
  "LocalDocs": {
    "Embeddings": {
      "Provider": "AzureOpenAI",
      "Dimension": 1536,
      "AzureOpenAI": {
        "Endpoint": "https://my-resource.openai.azure.com/",
        "ApiKey": "...",
        "DeploymentName": "text-embedding-3-small"
      }
    }
  }
}
```

---

### 3.4 Ollama (locale, privato)

Richiede Ollama in esecuzione in locale. Nessuna chiamata verso servizi cloud. Adatto per ambienti air-gapped o con requisiti di privacy.

**Modelli consigliati**:

| Modello | Dimensione | Note |
|--------|-----------|------|
| `nomic-embed-text` (default) | 768 | Ottimo rapporto qualità/velocità |
| `mxbai-embed-large` | 1024 | Qualità superiore, più lento |
| `all-minilm` | 384 | Più veloce, dimensione ridotta |
| `snowflake-arctic-embed` | 1024 | Buone performance multilingua |

> Quando si usa Ollama, impostare `Dimension` al valore corretto per il modello scelto (es. 768 per `nomic-embed-text`).

```json
{
  "LocalDocs": {
    "Embeddings": {
      "Provider": "Ollama",
      "Dimension": 768,
      "Ollama": {
        "Endpoint": "http://localhost:11434",
        "Model": "nomic-embed-text"
      }
    }
  }
}
```

---

## 4. Formati documento e chunking

### Formati supportati

| Estensione | Reader | Libreria | Limitazioni |
|-----------|--------|----------|------------|
| `.txt` | `PlainTextDocumentReader` | — | Solo UTF-8 |
| `.md` | `MarkdownDocumentReader` | — | Markdown raw, nessuna conversione HTML |
| `.pdf` | `PdfDocumentReader` | UglyToad.PdfPig | Solo testo nativo — **nessun OCR**: PDF da scansione/immagini restituiscono testo vuoto |
| `.docx` | `WordDocumentReader` | NPOI.XWPF | Solo formato Office 2007+ (Open XML) — **`.doc` legacy non supportato** |

Il `CompositeDocumentReader` aggrega tutti i reader e instrada automaticamente in base all'estensione.

### Chunking

Parametri configurabili sotto `LocalDocs:Chunking`:

| Parametro | Default | Descrizione |
|-----------|---------|------------|
| `MaxChunkSize` | `3000` | Dimensione massima di ciascun chunk in caratteri |
| `OverlapSize` | `300` | Numero di caratteri ripetuti tra chunk consecutivi (per mantenere il contesto ai confini) |

**ID chunk**: `{documentId}_chunk_{index}` (es. `abc123_chunk_0`, `abc123_chunk_1`, …)

**Esempio**: un documento di 9.000 caratteri con `MaxChunkSize=3000` e `OverlapSize=300` produce 4 chunk:
- Chunk 0: caratteri 0–2999
- Chunk 1: caratteri 2700–5699 (overlap di 300)
- Chunk 2: caratteri 5400–8399
- Chunk 3: caratteri 8100–8999 (chunk finale, più corto)

```json
{
  "LocalDocs": {
    "Chunking": {
      "MaxChunkSize": 3000,
      "OverlapSize": 300
    }
  }
}
```

---

## 5. File storage

I file originali (PDF, DOCX, ecc.) vengono salvati separatamente dal testo estratto e dai vettori.

### Panoramica

| Provider | Config value | Dove salva | Caso d'uso |
|----------|-------------|-----------|-----------|
| `Database` | `"Database"` | BLOB nella colonna `Documents.FileContent` | Default, setup semplice |
| `FileSystem` | `"FileSystem"` | Disco locale: `{BasePath}/{ProjectId}/{DocumentId}.{ext}` | File grandi, volumi separati |
| `AzureBlob` | `"AzureBlob"` | Azure Blob: `{ContainerName}/{ProjectId}/{DocumentId}.{ext}` | Cloud, scalabilità |

---

### 5.1 Database (default)

Il contenuto del file è salvato come BLOB nella tabella `Documents`. Nessuna configurazione aggiuntiva.

```json
{
  "LocalDocs": {
    "FileStorage": { "Provider": "Database" }
  }
}
```

---

### 5.2 FileSystem

Include protezione da path traversal: il percorso risolto viene validato per garantire che sia contenuto dentro `BasePath`.

| Parametro | Default | Descrizione |
|-----------|---------|------------|
| `BasePath` | `"DocumentFiles"` | Directory radice (relativa o assoluta) |
| `CreateDirectoryIfNotExists` | `true` | Crea le directory se non esistono |

```json
{
  "LocalDocs": {
    "FileStorage": {
      "Provider": "FileSystem",
      "FileSystem": {
        "BasePath": "/data/documents",
        "CreateDirectoryIfNotExists": true
      }
    }
  }
}
```

---

### 5.3 AzureBlob

Variabili d'ambiente supportate: `AZURE_STORAGE_CONNECTION_STRING`

Content-Type rilevato automaticamente dall'estensione: `.pdf` → `application/pdf`, `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `.txt` → `text/plain`, `.md` → `text/markdown`.

| Parametro | Default | Descrizione |
|-----------|---------|------------|
| `ConnectionString` | — | Connection string Azure Storage (o env var) |
| `ContainerName` | `"documents"` | Nome del container blob |
| `CreateContainerIfNotExists` | `true` | Crea il container se non esiste |

```json
{
  "LocalDocs": {
    "FileStorage": {
      "Provider": "AzureBlob",
      "AzureBlob": {
        "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;",
        "ContainerName": "documents",
        "CreateContainerIfNotExists": true
      }
    }
  }
}
```

---

## 6. Chat — provider e modalità

La funzionalità chat è **disabilitata per default** (`Enabled: false`). Quando abilitata, ogni progetto espone una pagina di chat.

### Modalità Classic RAG

1. La query dell'utente viene trasformata in embedding
2. Ricerca vettoriale → top-`MaxContextChunks` chunk rilevanti
3. I chunk vengono inseriti come contesto nel prompt al LLM
4. Il LLM risponde basandosi solo su quel contesto

### Modalità Agentica (`AgenticMode: true`)

1. Il LLM riceve la query e decide **autonomamente** se e quante volte cercare nei documenti
2. Può effettuare fino a **5 ricerche per sessione** (limite fisso nel codice)
3. La UI mostra i "thinking steps": icona lente per ogni ricerca, icona fulmine per l'analisi iniziale
4. Le risposte vengono trasmesse in streaming
5. **Requisito**: solo provider con tool-calling (OpenAI, AzureOpenAI, Ollama). Anthropic non supporta la modalità agentica — fallback automatico a Classic RAG.

### Parametri comuni

| Parametro | Default | Descrizione |
|-----------|---------|------------|
| `Enabled` | `false` | Abilita/disabilita la funzionalità chat |
| `Provider` | `"Fake"` | Provider LLM da usare |
| `MaxContextChunks` | `5` | Numero massimo di chunk passati come contesto (Classic RAG) |
| `AgenticMode` | `false` | Default per il toggle agentico nella UI (modificabile per sessione) |

### Provider chat

| Provider | Config value | Streaming | AgenticMode | Variabili d'ambiente |
|----------|-------------|-----------|-------------|---------------------|
| `Fake` | `"Fake"` | No | No | — |
| `OpenAI` | `"OpenAI"` | Sì (SSE) | Sì | `OPENAI_API_KEY` |
| `AzureOpenAI` | `"AzureOpenAI"` | Sì (SSE) | Sì | `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY` |
| `Anthropic` | `"Anthropic"` | Sì (SSE) | **No** | `ANTHROPIC_API_KEY` |
| `Ollama` | `"Ollama"` | Sì | Sì | — |

#### OpenAI

```json
{
  "LocalDocs": {
    "Chat": {
      "Enabled": true,
      "Provider": "OpenAI",
      "MaxContextChunks": 5,
      "AgenticMode": false,
      "OpenAI": {
        "ApiKey": "sk-...",
        "Model": "gpt-4o-mini"
      }
    }
  }
}
```

Modelli consigliati: `gpt-4o-mini` (veloce/economico), `gpt-4o` (qualità superiore).

#### AzureOpenAI

```json
{
  "LocalDocs": {
    "Chat": {
      "Enabled": true,
      "Provider": "AzureOpenAI",
      "AzureOpenAI": {
        "Endpoint": "https://my-resource.openai.azure.com/",
        "ApiKey": "...",
        "DeploymentName": "gpt-4o"
      }
    }
  }
}
```

#### Anthropic

```json
{
  "LocalDocs": {
    "Chat": {
      "Enabled": true,
      "Provider": "Anthropic",
      "Anthropic": {
        "ApiKey": "sk-ant-...",
        "Model": "claude-sonnet-4-6",
        "MaxTokens": 4096
      }
    }
  }
}
```

Modelli Claude disponibili: `claude-opus-4-6`, `claude-sonnet-4-6`, `claude-haiku-4-5-20251001`.
`MaxTokens` (default `4096`): massimo token generati per risposta.

#### Ollama

```json
{
  "LocalDocs": {
    "Chat": {
      "Enabled": true,
      "Provider": "Ollama",
      "Ollama": {
        "Endpoint": "http://localhost:11434",
        "Model": "llama3"
      }
    }
  }
}
```

Modelli consigliati: `llama3`, `mistral`, `phi3`, `qwen2`.

---

## 7. MCP Tools — riferimento completo

**Endpoint MCP**: `http://localhost:5024/mcp`
**Autenticazione**: Bearer token (se `Mcp:RequireAuthentication: true`). Token gestiti in `/settings/tokens` nella UI.

### Elenco strumenti

| Tool | Operazione | Parametri obbligatori | Parametri opzionali |
|------|-----------|----------------------|--------------------:|
| `list_projects` | Elenca tutti i progetti | — | — |
| `create_project` | Crea un nuovo progetto | `name` | `description` |
| `get_project` | Dettagli progetto + lista documenti | `projectIdOrName` (ID o nome) | — |
| `delete_project` | Elimina progetto e tutti i documenti | `projectId` | — |
| `add_document` | Aggiunge documento e lo indicizza | `projectId`, `fileName`, `content` | — |
| `update_document` | Crea nuova versione del documento | `documentId`, `content` | `fileName` |
| `list_documents` | Elenca documenti di un progetto | `projectId` | `includeSuperseded` (default: `false`) |
| `get_document` | Metadati + anteprima testo (500 char) | `documentId` | — |
| `get_document_content` | Testo estratto completo | `documentId` | — |
| `delete_document` | Elimina documento e tutti i chunk | `documentId` | — |
| `search_docs` | Ricerca semantica | `query` | `projectId`, `limit` (default: 5, max: 20) |

---

### Dettaglio strumenti

#### `search_docs`

Ricerca semantica: la query viene convertita in embedding e confrontata con tutti i vettori indicizzati.

```
query        — stringa in linguaggio naturale
projectId    — (opzionale) filtra la ricerca a un singolo progetto
limit        — numero di risultati (default: 5, min: 1, max: 20)
```

Output: lista di risultati con `Score` (0–1, più alto = più rilevante), nome file sorgente, ID documento, contenuto del chunk.

#### `add_document`

```
projectId  — ID del progetto destinazione (deve esistere, crearlo prima con create_project)
fileName   — nome del file inclusa estensione (es. "spec.pdf", "notes.md")
content    — testo in chiaro per .txt/.md; base64 per .pdf/.docx
```

Formati accettati: `.txt`, `.md`, `.pdf`, `.docx`
Per file binari (PDF, DOCX): il campo `content` deve contenere il file codificato in **base64**.

#### `update_document`

Crea una **nuova versione** del documento: incrementa `VersionNumber`, imposta `ParentDocumentId`, rimuove i chunk della versione precedente dal vector store e li reindicizza con il nuovo contenuto.

```
documentId — ID del documento da aggiornare (deve essere la versione attiva, non superseded)
content    — nuovo contenuto testuale (testo o Markdown)
fileName   — (opzionale) nuovo nome file; se omesso, per PDF/DOCX il nome viene mantenuto con estensione .md
```

> I file binari (PDF/DOCX) vengono salvati come testo Markdown nella nuova versione, poiché il contenuto viene fornito come testo dall'LLM.

#### `delete_project`

**Irreversibile**: elimina il progetto, tutti i documenti associati, i loro chunk e i vettori.

#### `delete_document`

**Irreversibile**: elimina il documento, tutti i chunk e i vettori. Non elimina le versioni collegate (parent/children).

---

## 8. Domain model

### Entità principali

```
Project
├── Id            string (GUID)        — identificatore univoco
├── Name          string               — nome univoco del progetto
├── Description   string?              — descrizione opzionale
├── CreatedAt     DateTimeOffset       — timestamp UTC creazione
└── UpdatedAt     DateTimeOffset?      — timestamp UTC ultima modifica

Document
├── Id                  string (GUID)  — identificatore univoco
├── ProjectId           string         — FK → Project.Id
├── FileName            string         — nome file originale (es. "FRD_v1.5.pdf")
├── FileExtension       string         — estensione (es. ".pdf", ".docx")
├── FileContent         byte[]?        — contenuto binario (null se FileStorage != Database)
├── FileStorageLocation string?        — percorso su FileSystem/AzureBlob
├── FileSizeBytes       long           — dimensione in byte
├── ExtractedText       string         — testo estratto per chunking/ricerca
├── ContentHash         string?        — SHA-256 del file (per deduplicazione)
├── Metadata            Dictionary?    — coppie chiave-valore aggiuntive
├── VersionNumber       int            — inizia a 1, incrementa a ogni update
├── ParentDocumentId    string?        — ID versione precedente (null per v1)
├── IsSuperseded        bool           — true se sostituito da versione più recente
├── CreatedAt           DateTimeOffset
└── UpdatedAt           DateTimeOffset?

DocumentChunk
├── Id          string  — formato: {documentId}_chunk_{index}
├── DocumentId  string  — FK → Document.Id
└── Content     string  — testo del chunk (≤ MaxChunkSize caratteri)
```

### Versioning dei documenti

```
Document v1 (Id: "abc")
  IsSuperseded: false
  ParentDocumentId: null
         │
         │  update_document("abc", newContent)
         ▼
Document v2 (Id: "xyz")
  IsSuperseded: false
  ParentDocumentId: "abc"
  VersionNumber: 2

Document v1 (Id: "abc") — aggiornato
  IsSuperseded: true   ← i chunk sono rimossi dal vector store
  VersionNumber: 1     ← metadati e testo estratto preservati
```

- La ricerca semantica considera solo documenti con `IsSuperseded = false`
- `list_documents` nasconde i superseded per default; passare `includeSuperseded=true` per vederli
- `delete_document` elimina fisicamente il documento; non cancella la catena di versioni

---

## 9. Autenticazione

### UI (Blazor)

- **Tipo**: cookie-based session (ASP.NET Core)
- **Scadenza**: 7 giorni, sliding expiration
- **Cookie**: HttpOnly, SameSite=Strict
- **Login**: `/login` con username e password da `appsettings.json`

```json
{
  "LocalDocs": {
    "Authentication": {
      "Username": "admin",
      "Password": "admin"
    }
  }
}
```

> Cambiare username e password in produzione. Non committare credenziali nel repository.

### MCP endpoint

- **Config**: `LocalDocs:Mcp:RequireAuthentication` (default: `true` in produzione, `false` in Development)
- **Metodo**: Bearer token nell'header `Authorization`
- **Gestione token**: pagina `/settings/tokens` nella UI
  - Creare token con nome descrittivo
  - Revocare token compromessi
  - Token mostrano solo i primi 4 caratteri (prefix) — il valore completo è mostrato solo alla creazione

**Configurazione Claude Code**:
```json
{
  "mcpServers": {
    "local-docs": {
      "type": "http",
      "url": "http://localhost:5024/mcp",
      "headers": {
        "Authorization": "Bearer <token>"
      }
    }
  }
}
```

---

## 10. Limiti e parametri fissi

| Parametro | Valore | Modificabile | Dove |
|-----------|--------|-------------|------|
| `MaxContextChunks` | 5 | Sì | `Chat:MaxContextChunks` |
| `MaxChunkSize` | 3.000 caratteri | Sì | `Chunking:MaxChunkSize` |
| `OverlapSize` | 300 caratteri | Sì | `Chunking:OverlapSize` |
| `Anthropic:MaxTokens` | 4.096 | Sì | `Chat:Anthropic:MaxTokens` |
| `search_docs` limit default | 5 | Per chiamata | parametro `limit` |
| `search_docs` limit massimo | 20 | No (fisso nel codice) | — |
| File per upload (UI) | 10 | No | — |
| Dimensione massima file (UI) | 50 MB | No | — |
| Ricerche agentiche per sessione | 5 | No | — |
| Cookie session duration | 7 giorni | No | — |

---

## 11. Configurazioni complete per scenario

### Scenario 1 — Sviluppo locale (zero dipendenze esterne)

```json
{
  "ConnectionStrings": { "LocalDocs": "Data Source=localdocs.db" },
  "LocalDocs": {
    "Authentication": { "Username": "admin", "Password": "admin" },
    "Mcp": { "RequireAuthentication": false },
    "Embeddings": { "Provider": "Fake", "Dimension": 1536 },
    "Storage": { "Provider": "Sqlite" },
    "FileStorage": { "Provider": "Database" },
    "Chunking": { "MaxChunkSize": 3000, "OverlapSize": 300 },
    "Chat": { "Enabled": false }
  }
}
```

---

### Scenario 2 — Privacy / self-hosted (tutto locale con Ollama)

```json
{
  "ConnectionStrings": { "LocalDocs": "Data Source=/data/localdocs.db" },
  "LocalDocs": {
    "Authentication": { "Username": "admin", "Password": "cambia-questa-password" },
    "Embeddings": {
      "Provider": "Ollama",
      "Dimension": 768,
      "Ollama": { "Endpoint": "http://localhost:11434", "Model": "nomic-embed-text" }
    },
    "Storage": {
      "Provider": "SqliteHnsw",
      "Hnsw": {
        "IndexPath": "/data/hnsw_index.bin",
        "MaxConnections": 16,
        "EfConstruction": 200,
        "EfSearch": 50
      }
    },
    "FileStorage": {
      "Provider": "FileSystem",
      "FileSystem": { "BasePath": "/data/documents" }
    },
    "Chat": {
      "Enabled": true,
      "Provider": "Ollama",
      "MaxContextChunks": 5,
      "AgenticMode": false,
      "Ollama": { "Endpoint": "http://localhost:11434", "Model": "llama3" }
    }
  }
}
```

---

### Scenario 3 — Produzione standard (OpenAI + HNSW)

```json
{
  "ConnectionStrings": { "LocalDocs": "Data Source=/data/localdocs.db" },
  "LocalDocs": {
    "Authentication": { "Username": "admin", "Password": "cambia-questa-password" },
    "Embeddings": {
      "Provider": "OpenAI",
      "Dimension": 1536,
      "OpenAI": { "Model": "text-embedding-3-small" }
    },
    "Storage": {
      "Provider": "SqliteHnsw",
      "Hnsw": {
        "IndexPath": "/data/hnsw_index.bin",
        "MaxConnections": 24,
        "EfConstruction": 200,
        "EfSearch": 100
      }
    },
    "FileStorage": {
      "Provider": "FileSystem",
      "FileSystem": { "BasePath": "/data/documents" }
    },
    "Chat": {
      "Enabled": true,
      "Provider": "OpenAI",
      "MaxContextChunks": 5,
      "AgenticMode": true,
      "OpenAI": { "Model": "gpt-4o-mini" }
    }
  }
}
```

> Impostare `OPENAI_API_KEY` come variabile d'ambiente — non committare la chiave in appsettings.

---

### Scenario 4 — Enterprise (Azure OpenAI + SQL Server + Azure Blob)

```json
{
  "ConnectionStrings": {
    "LocalDocs": "Server=myserver.database.windows.net;Database=localdocs;Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;"
  },
  "LocalDocs": {
    "Authentication": { "Username": "admin", "Password": "cambia-questa-password" },
    "Embeddings": {
      "Provider": "AzureOpenAI",
      "Dimension": 1536,
      "AzureOpenAI": {
        "DeploymentName": "text-embedding-3-small"
      }
    },
    "Storage": {
      "Provider": "SqlServer",
      "SqlServer": {
        "Schema": "dbo",
        "TableName": "chunk_embeddings",
        "UseVectorIndex": true,
        "DistanceMetric": "cosine"
      }
    },
    "FileStorage": {
      "Provider": "AzureBlob",
      "AzureBlob": {
        "ContainerName": "documents",
        "CreateContainerIfNotExists": true
      }
    },
    "Chat": {
      "Enabled": true,
      "Provider": "AzureOpenAI",
      "MaxContextChunks": 8,
      "AgenticMode": true,
      "AzureOpenAI": {
        "DeploymentName": "gpt-4o"
      }
    }
  }
}
```

> `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY` e `AZURE_STORAGE_CONNECTION_STRING` come variabili d'ambiente.

---

### Scenario 5 — Chat con Anthropic Claude (embeddings OpenAI + HNSW)

```json
{
  "ConnectionStrings": { "LocalDocs": "Data Source=/data/localdocs.db" },
  "LocalDocs": {
    "Authentication": { "Username": "admin", "Password": "cambia-questa-password" },
    "Embeddings": {
      "Provider": "OpenAI",
      "Dimension": 1536,
      "OpenAI": { "Model": "text-embedding-3-small" }
    },
    "Storage": {
      "Provider": "SqliteHnsw",
      "Hnsw": { "IndexPath": "/data/hnsw_index.bin" }
    },
    "FileStorage": {
      "Provider": "FileSystem",
      "FileSystem": { "BasePath": "/data/documents" }
    },
    "Chat": {
      "Enabled": true,
      "Provider": "Anthropic",
      "MaxContextChunks": 10,
      "AgenticMode": false,
      "Anthropic": {
        "Model": "claude-sonnet-4-6",
        "MaxTokens": 4096
      }
    }
  }
}
```

> `OPENAI_API_KEY` e `ANTHROPIC_API_KEY` come variabili d'ambiente.
> `AgenticMode` non supportato con Anthropic — impostare sempre `false`.
