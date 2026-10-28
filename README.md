# Brothers Care API

Fundação técnica da API SaaS do Brothers Care. Esta etapa estabelece arquitetura, configuração HTTP, qualidade e documentação; ela não contém funcionalidades de negócio.

## Requisitos

- .NET SDK 10.0.301 ou patch compatível da linha 10.0, conforme `global.json`.
- SQL Server é necessário para executar funcionalidades persistentes quando forem adicionadas. A fundação HTTP e os health checks continuam podendo ser executados sem banco configurado.

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

Sem `Cors:AllowedOrigins`, nenhuma política CORS é habilitada. Para habilitar a persistência, informe a conexão somente por variável de ambiente ou user secrets:

```powershell
$env:ConnectionStrings__BrothersCare = "Server=localhost;Database=BrothersCare;Trusted_Connection=True;TrustServerCertificate=True"
```

O provider SQL Server e o `DbContext` ficam em Infrastructure. Não há credenciais de banco versionadas.

## Migrações

As migrações são geradas a partir do projeto Infrastructure e devem ser revisadas em pull request. Para aplicar uma migração em ambiente local, configure `ConnectionStrings__BrothersCare` e execute:

```powershell
dotnet tool restore
dotnet ef database update --project src/BrothersCare.Infrastructure --startup-project src/BrothersCare.Api
```

## Primeiro superadministrador

Depois de aplicar a migração que inclui `platform_administrators`, o primeiro superadministrador pode ser provisionado sem gravar dados pessoais ou credenciais no repositório. Configure somente no ambiente seguro onde o comando será executado:

```powershell
$env:Bootstrap__SuperAdministrator__Email = "admin@empresa.exemplo"
$env:Bootstrap__SuperAdministrator__DisplayName = "Administrador da plataforma"
$env:Bootstrap__SuperAdministrator__Confirmation = "create-super-administrator"
dotnet run --project src/BrothersCare.Api -- --bootstrap-super-administrator
```

O comando é idempotente para o mesmo usuário ativo. Ele cria a identidade global e o vínculo `platform_administrators`; não cria senha, token ou sessão. Esses elementos pertencem à futura implementação de autenticação.

## Ativação do superadministrador

Após o bootstrap e a migration de autenticação, emita um token temporário em um terminal confiável. O token é mostrado uma única vez no console, não é registrado pela aplicação e expira em 30 minutos por padrão. Não o inclua em e-mails, logs, tickets ou repositório.

```powershell
$env:Bootstrap__AccountActivation__Email = "admin@empresa.exemplo"
$env:Bootstrap__AccountActivation__Confirmation = "issue-account-activation-token"
$env:Bootstrap__AccountActivation__LifetimeMinutes = "30"
dotnet run --project src/BrothersCare.Api -- --issue-super-administrator-activation-token
```

O token é aceito pelo caso de uso de ativação; o endpoint HTTP será disponibilizado na etapa seguinte. Emitir outro token revoga imediatamente os anteriores ainda ativos para o mesmo superadministrador.

## Projetos

- `src/BrothersCare.Domain`: modelo e regras do domínio, sem infraestrutura.
- `src/BrothersCare.Application`: contratos e coordenação dos futuros casos de uso.
- `src/BrothersCare.Infrastructure`: futuros adaptadores de persistência e integrações.
- `src/BrothersCare.Api`: composição e fronteira HTTP.
- `tests/BrothersCare.ArchitectureTests`: protege a direção das dependências.
- `tests/BrothersCare.Api.IntegrationTests`: verifica a fundação HTTP pelo processo real de teste.

As versões NuGet ficam em `Directory.Packages.props`; regras comuns de compilação ficam em `Directory.Build.props`.

## Regras de negócio

A proposta de regras e transições de estado para pacientes, profissionais, agenda, evoluções, financeiro e assinaturas está em [docs/mvp-business-rules.md](docs/mvp-business-rules.md). O documento distingue regras propostas de comportamentos ainda não aprovados.

As convenções propostas para OpenAPI, contratos HTTP, paginação, filtros, datas, valores monetários e erros estão em [docs/http-api-contracts.md](docs/http-api-contracts.md).

A especificação completa para login, sessão, expiração, logout, ativação e recuperação de acesso está em [docs/authentication-implementation.md](docs/authentication-implementation.md).

## E-mail transacional

Convites e recuperação de senha só são enviados quando `Email:Smtp:Enabled` estiver habilitado e toda a configuração SMTP estiver presente. Configure os segredos pelo ambiente ou user secrets; nunca os versione.

```powershell
$env:Email__Smtp__Enabled = "true"
$env:Email__Smtp__Host = "mail5015.site4now.net"
$env:Email__Smtp__Port = "587"
$env:Email__Smtp__UserName = "noreply@brothersdigital.tec.br"
$env:Email__Smtp__Password = "senha-da-caixa"
$env:Email__Smtp__FromAddress = "noreply@brothersdigital.tec.br"
$env:Email__Smtp__FromName = "Brothers Care"
$env:Email__Links__ActivationUrl = "https://app.seu-dominio.com/ativar-conta"
$env:Email__Links__PasswordRecoveryUrl = "https://app.seu-dominio.com/recuperar-senha"
```

O adaptador usa SMTP com TLS. Para o serviço MyASP, use a porta 587 e o host seguro informado no painel. As URLs devem apontar para telas do frontend que recebem o parâmetro `token` e chamam os endpoints de ativação ou recuperação.

## Dependências adicionais

`Swashbuckle.AspNetCore` publica o documento e a UI Swagger apenas em desenvolvimento. `Microsoft.AspNetCore.Mvc.Testing` hospeda a API em memória nos testes de integração. xUnit e Microsoft.NET.Test.Sdk formam a infraestrutura mínima de testes. Não foi adicionada biblioteca de arquitetura: os testes usam reflexão do próprio .NET.

## Fora do escopo atual

O modelo inicial contém tenants, unidades, usuários, memberships, pacientes, profissionais, vínculos de cuidado, atendimentos e evoluções para suportar as primeiras migrações. Ainda não existem endpoints, autenticação, autorização, casos de uso nem operações funcionais para esses domínios. Consulte `ARCHITECTURE.md` antes de ampliar o modelo e `AGENTS.md` antes de contribuir com assistência de IA.
