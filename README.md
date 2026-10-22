# Brothers Care API

Fundação técnica da API SaaS do Brothers Care. Esta etapa estabelece arquitetura, configuração HTTP, qualidade e documentação; ela não contém funcionalidades de negócio.

## Requisitos

- .NET SDK 10.0.301 ou patch compatível da linha 10.0, conforme `global.json`.
- PostgreSQL não é necessário nesta etapa.

## Comandos locais

```powershell
dotnet restore BrothersCare.slnx
dotnet build BrothersCare.slnx --no-restore
dotnet test BrothersCare.slnx --no-build
dotnet format BrothersCare.slnx --verify-no-changes --no-restore
dotnet run --project src/BrothersCare.Api
```

Em `Development`, a interface Swagger fica em `/swagger`. Os únicos endpoints funcionais são:

- `GET /health/live`: confirma que o processo HTTP está ativo;
- `GET /health/ready`: executará verificações de dependências marcadas com a tag `ready` quando elas existirem.

## Configuração local

A configuração segue o padrão do ASP.NET Core: `appsettings.json`, arquivo do ambiente, variáveis de ambiente e user secrets. Não versione credenciais. Use nomes hierárquicos com `__` em variáveis de ambiente.

Exemplo de CORS para desenvolvimento:

```powershell
$env:Cors__AllowedOrigins__0 = "https://localhost:3000"
dotnet run --project src/BrothersCare.Api
```

Sem `Cors:AllowedOrigins`, nenhuma política CORS é habilitada. Uma futura conexão poderá usar o placeholder `ConnectionStrings__BrothersCare`; ainda não há provider, contexto EF ou acesso a banco configurado.

## Projetos

- `src/BrothersCare.Domain`: modelo e regras do domínio, sem infraestrutura.
- `src/BrothersCare.Application`: contratos e coordenação dos futuros casos de uso.
- `src/BrothersCare.Infrastructure`: futuros adaptadores de persistência e integrações.
- `src/BrothersCare.Api`: composição e fronteira HTTP.
- `tests/BrothersCare.ArchitectureTests`: protege a direção das dependências.
- `tests/BrothersCare.Api.IntegrationTests`: verifica a fundação HTTP pelo processo real de teste.

As versões NuGet ficam em `Directory.Packages.props`; regras comuns de compilação ficam em `Directory.Build.props`.

## Dependências adicionais

`Swashbuckle.AspNetCore` publica o documento e a UI Swagger apenas em desenvolvimento. `Microsoft.AspNetCore.Mvc.Testing` hospeda a API em memória nos testes de integração. xUnit e Microsoft.NET.Test.Sdk formam a infraestrutura mínima de testes. Não foi adicionada biblioteca de arquitetura: os testes usam reflexão do próprio .NET.

## Fora do escopo atual

Não existem cadastro, autenticação, autorização, tenants, clínicas, pacientes, agenda, financeiro, entidades, schema, migrações, EF Core nem integração PostgreSQL. Consulte `ARCHITECTURE.md` antes de introduzir qualquer um desses itens e `AGENTS.md` antes de contribuir com assistência de IA.
