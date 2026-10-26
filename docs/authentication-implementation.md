# Backend — Implementar autenticação

## Objetivo

Implementar autenticação server-side para Brothers Care com login, consulta de sessão, seleção segura de tenant, expiração, logout com invalidação e ativação inicial de conta. O frontend não armazena token, senha, sessão ou informação clínica em `localStorage` ou `sessionStorage`.

Esta especificação complementa [mvp-business-rules.md](mvp-business-rules.md) e [http-api-contracts.md](http-api-contracts.md). Onde houver conflito, as regras de tenant, autorização e privacidade prevalecem.

## Decisões obrigatórias

| Tema | Decisão |
| --- | --- |
| Sessão | Cookie opaco, aleatório e `HttpOnly`; o servidor persiste somente o hash do segredo da sessão. |
| Credencial | Senha armazenada somente como hash versionado. Usar `PasswordHasher<TUser>` do ASP.NET Core Identity ou Argon2id com parâmetros aprovados; nunca usar hash próprio, SHA, MD5 ou criptografia reversível. |
| Tenant ativo | Mantido na sessão no servidor. A troca ocorre por endpoint autenticado e valida membership ativa; o cliente não envia `tenantId` como fonte de autorização para as operações de clínica. |
| Superadministrador | `platform_administrators` é global e separado de `tenant_memberships`. Esse papel não concede acesso clínico automático. |
| Criação de usuários clínicos | A empresa deve existir antes do convite. O usuário é uma identidade global e recebe acesso por `tenant_membership`; não criar uma identidade duplicada para a mesma pessoa. |
| Expiração | 30 minutos de inatividade e 12 horas de duração máxima, configuráveis por ambiente. |
| Logout | Revoga a sessão no banco e expira o cookie. |
| Recuperação/ativação | Token de uso único, aleatório, com hash persistido, expiração curta e nunca incluído em logs. |
| CORS e cookies | Origens explícitas, `AllowCredentials` apenas para origens confiáveis e nunca `AllowAnyOrigin` junto com credenciais. |

## Fora do escopo desta tarefa

- Login social, SSO corporativo, MFA e passkeys.
- Integração definitiva de e-mail ou SMS; a interface para envio pode ser criada, mas o provedor depende de tarefa própria.
- Delegação de suporte da Brothers Digital para acesso a dados clínicos.
- Autorização completa dos domínios clínicos e financeiros. Esta tarefa fornece identidade e contexto; cada caso de uso ainda valida capacidades, unidade e vínculo de cuidado.

## Modelo de dados

Criar as entidades e migrações abaixo em `Domain` e `Infrastructure`. Os nomes de colunas e tabelas devem seguir o padrão já existente (`snake_case` no banco e propriedades C# em PascalCase).

### `user_credentials`

Relação 1:1 com `users`. Esta tabela não possui `tenant_id` porque a identidade é global.

| Coluna | Tipo | Regra |
| --- | --- | --- |
| `id` | UUID | Chave primária gerada pela aplicação. |
| `user_id` | UUID | Único e FK para `users`. |
| `password_hash` | texto | Obrigatório após ativação; formato do hasher escolhido. |
| `status` | inteiro/enum | `PendingActivation`, `Active`, `Locked`, `Disabled`. |
| `failed_sign_in_count` | inteiro | Inicia em zero; nunca negativo. |
| `lockout_ends_at` | datetimeoffset nulo | Obrigatório enquanto o estado for `Locked`. |
| `password_changed_at` | datetimeoffset nulo | Preenchido ao criar ou trocar senha. |
| `created_at` | datetimeoffset | Obrigatório. |
| `updated_at` | datetimeoffset | Obrigatório. |

Índices e restrições:

- índice único em `user_id`;
- índice em `(status, lockout_ends_at)` para limpeza e verificação operacional;
- não salvar senha, confirmação de senha, token de sessão ou token de ativação nesta tabela.

### `user_sessions`

Cada login cria uma sessão independente. A tabela não possui `tenant_id`; a escolha de tenant não altera a identidade global.

| Coluna | Tipo | Regra |
| --- | --- | --- |
| `id` | UUID | Parte pública do identificador da sessão. |
| `user_id` | UUID | FK para `users`. |
| `secret_hash` | binário ou texto | Hash SHA-256 do segredo aleatório de 32 bytes; único. |
| `active_tenant_id` | UUID nulo | Tenant selecionado após membership validada; FK para `tenants`. |
| `created_at` | datetimeoffset | Obrigatório. |
| `last_seen_at` | datetimeoffset | Atualizado no máximo uma vez a cada cinco minutos. |
| `idle_expires_at` | datetimeoffset | Início + janela de inatividade renovável até o limite absoluto. |
| `absolute_expires_at` | datetimeoffset | Não é renovado. |
| `revoked_at` | datetimeoffset nulo | Preenchido no logout, troca de senha, desativação ou incidente. |
| `revocation_reason` | enum/texto nulo | `Logout`, `PasswordChanged`, `AccountDisabled`, `SecurityIncident`, `Expired`. |

Índices e restrições:

- índice único em `secret_hash`;
- índice em `(user_id, revoked_at, absolute_expires_at)`;
- índice em `active_tenant_id`;
- `idle_expires_at` não pode ultrapassar `absolute_expires_at`;
- uma sessão revogada nunca volta a ficar válida.

### `account_action_tokens`

Usar para ativação inicial, recuperação de senha e convite de acesso até existir uma necessidade concreta de separar tabelas.

| Coluna | Tipo | Regra |
| --- | --- | --- |
| `id` | UUID | Chave primária. |
| `user_id` | UUID | FK para `users`. |
| `purpose` | enum | `AccountActivation`, `PasswordRecovery`, `InvitationAcceptance`. |
| `secret_hash` | binário ou texto | Hash SHA-256 do token aleatório; único. |
| `created_at` | datetimeoffset | Obrigatório. |
| `expires_at` | datetimeoffset | Obrigatório; padrão de 30 minutos. |
| `consumed_at` | datetimeoffset nulo | Preenchido no primeiro uso válido. |
| `revoked_at` | datetimeoffset nulo | Preenchido quando for substituído, expirado ou invalidado. |

Regras:

- token tem pelo menos 32 bytes criptograficamente aleatórios e é codificado em Base64Url;
- apenas o hash é persistido;
- o token em texto puro existe somente no momento de enviar a instrução de ativação ou recuperação;
- criar novo token para o mesmo usuário e finalidade revoga todos os anteriores ainda ativos;
- consumo, expiração e revogação tornam o token inutilizável permanentemente.

### Alterações em entidades existentes

- `users` continua global e não recebe `tenant_id`.
- `platform_administrators` continua global e não recebe `tenant_id`.
- `tenant_memberships` permanece a única fonte de papéis de clínica. Não incluir `SuperAdministrator` nesse enum.
- Expandir o estado de `tenant_memberships` de `Active` e `Inactive` para `Invited`, `Active`, `Suspended` e `Revoked`. Criar uma migração explícita para preservar e converter os dados existentes: `Active` vira `Active` e `Inactive` vira `Revoked`.
- `tenant_memberships` precisa continuar única por `(tenant_id, user_id)`. Para convidar novamente uma pessoa com vínculo `Revoked`, reativar o vínculo de forma auditada ou criar uma regra explícita de novo convite; nunca criar duplicata.
- `active_tenant_id` de uma sessão só pode ser definido se existir `tenant_membership` ativa para `user_id` e `tenant_id`.

## Fluxo de criação de empresa e usuários

### Primeiro administrador de uma empresa

1. Um superadministrador autenticado cria o `tenant` e, quando aplicável, a primeira unidade.
2. Ele informa o nome e o e-mail do primeiro administrador da clínica.
3. O servidor busca `users` por e-mail normalizado.
4. Se a identidade ainda não existir, cria `User` global e `UserCredential` em `PendingActivation`.
5. O servidor cria `tenant_membership` com papel `ClinicAdministrator` e estado `Invited`.
6. O servidor cria `AccountActionToken` com finalidade `InvitationAcceptance`, vinculado à identidade e ao convite.
7. O token é entregue pelo canal de comunicação aprovado. A resposta da API não devolve o token.
8. A pessoa aceita o convite, define a senha se necessário e a membership muda para `Active` na mesma transação que consome o token.
9. Somente depois da ativação a pessoa pode selecionar o tenant e operar a clínica.

O superadministrador pode criar empresa e convite, mas não se torna administrador clínico nem ganha acesso a dados do tenant por essa ação.

### Convites feitos pelo administrador da clínica

1. O administrador autenticado só pode convidar pessoas para seu `active_tenant_id` e unidades que sua membership permite administrar.
2. Ele informa e-mail, nome de exibição, papel de clínica e unidades iniciais autorizadas.
3. O servidor localiza ou cria a identidade global sem duplicar `users`.
4. O servidor cria ou atualiza a membership em estado `Invited`, cria os vínculos de unidade pendentes e emite token de convite.
5. Ao aceitar o convite, a membership e os vínculos de unidade passam a estar disponíveis conforme as regras aprovadas.

Se a pessoa já participa de outra empresa, o convite cria apenas nova `tenant_membership`; ela mantém a mesma identidade e pode selecionar um tenant ativo por vez. A existência da pessoa em outra empresa não é exposta ao administrador que a convida.

### Estados da membership

| Estado atual | Ação | Próximo estado | Efeito de acesso |
| --- | --- | --- | --- |
| `Invited` | Aceite válido do convite | `Active` | Acesso liberado conforme papel e unidades. |
| `Invited` | Revogação do convite | `Revoked` | Sem acesso; token é revogado. |
| `Active` | Suspensão autorizada | `Suspended` | Sessões daquele tenant são invalidadas; sem acesso ao tenant. |
| `Active` | Revogação do vínculo | `Revoked` | Sessões daquele tenant são invalidadas; sem acesso ao tenant. |
| `Suspended` | Reativação autorizada | `Active` | Acesso restaurado, sem restaurar tokens expirados. |
| `Suspended` | Revogação do vínculo | `Revoked` | Sem acesso. |
| `Revoked` | Novo convite autorizado | `Invited` | Exige novo token de aceite. |

Nenhuma membership em estado `Invited`, `Suspended` ou `Revoked` pode ser usada como tenant ativo ou satisfazer autorização.

## Estados e transições

### Credencial

| Estado atual | Ação | Próximo estado |
| --- | --- | --- |
| `PendingActivation` | Ativação válida e definição de senha | `Active` |
| `Active` | Número máximo de tentativas falhas | `Locked` |
| `Locked` | Expiração do bloqueio ou desbloqueio administrativo auditado | `Active` |
| `Active` ou `Locked` | Desativação da conta | `Disabled` |
| `Disabled` | Reativação administrativa auditada | `Active` ou `PendingActivation`, conforme decisão de negócio |

Parâmetros iniciais propostos: bloqueio após cinco falhas consecutivas por 15 minutos. Eles devem ser configuráveis por ambiente e não hardcoded no caso de uso.

### Sessão

| Estado atual | Evento | Resultado |
| --- | --- | --- |
| Válida | Requisição autenticada | Atualiza `last_seen_at` e renova apenas `idle_expires_at`, respeitando o limite absoluto. |
| Válida | Logout | Revogada com motivo `Logout`; cookie expirado. |
| Válida | Troca ou recuperação de senha | Todas as sessões do usuário são revogadas, exceto uma eventual nova sessão emitida após a troca. |
| Válida | Conta bloqueada ou desativada | Revogada; próximas requisições retornam `401`. |
| Válida | Tempo ocioso ou absoluto atingido | Revogada ou considerada inválida; cookie expirado. |
| Revogada ou expirada | Qualquer requisição | `401 Unauthorized`; nunca é reativada. |

## Formato do cookie

- Nome configurável, padrão `bc_session`.
- Valor opaco no formato `<session-id>.<secret-base64url>`.
- `HttpOnly=true`, `Secure=true`, `SameSite=Lax`, `Path=/api`.
- Não definir `Domain` salvo necessidade operacional aprovada; preferir cookie host-only.
- Em ambiente local, permitir `Secure=false` somente por configuração de desenvolvimento não versionada.
- Se frontend e API precisarem operar em contexto realmente cross-site, a mudança para `SameSite=None` exige HTTPS, CORS com origem explícita e revisão de CSRF antes da liberação.
- Não usar JWT como sessão persistida no navegador para este MVP. Se JWT for necessário futuramente para integrações, ele terá contrato e rotação próprios.

## Endpoints HTTP

As rotas ficam sob `/api/v1/auth` e usam Problem Details conforme [http-api-contracts.md](http-api-contracts.md).

### `POST /api/v1/auth/login`

Request:

```json
{
  "email": "admin@empresa.exemplo",
  "password": "senha-informada-somente-nesta-requisicao"
}
```

Comportamento:

1. Validar formato, limite de tamanho e presença de e-mail e senha.
2. Aplicar rate limit por IP e por e-mail normalizado sem diferenciar usuário existente de inexistente.
3. Localizar `users` pelo e-mail normalizado e `user_credentials` correspondente.
4. Verificar bloqueio, estado da credencial e hash da senha.
5. Para qualquer falha de credencial, retornar o mesmo `401` genérico, incrementar tentativas somente quando houver conta ativa e não registrar a senha.
6. Em sucesso, zerar tentativas, criar sessão, definir cookie e retornar resumo da sessão sem dados clínicos.

Resposta `200`:

```json
{
  "user": {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "displayName": "Administrador da plataforma",
    "email": "admin@empresa.exemplo"
  },
  "platformAdministrator": {
    "role": "SuperAdministrator"
  },
  "memberships": [],
  "activeTenant": null,
  "expiresAt": "2026-10-26T22:00:00-03:00"
}
```

### `GET /api/v1/auth/session`

Exige cookie de sessão válido. Retorna o mesmo resumo de identidade atualizado, memberships ativas e tenant ativo. Não retorna hash, token, permissões clínicas derivadas, conteúdo clínico ou informações de outros usuários.

Retornar `401` quando a sessão não existir, estiver revogada ou expirada. O frontend deve tratar esse retorno como encerramento de sessão e redirecionar para login, sem tentar renovar por conta própria.

### `PUT /api/v1/auth/active-tenant`

Request:

```json
{
  "tenantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
}
```

Comportamento:

- exige sessão válida;
- valida UUID e membership ativa para o tenant solicitado;
- grava `active_tenant_id` na sessão;
- retorna o resumo atualizado de sessão;
- retorna `404` se o tenant não puder ser selecionado pela identidade atual.

`tenantId` só é aceito nesta operação de seleção. As demais rotas de clínica usam o tenant ativo validado no servidor.

### `POST /api/v1/auth/logout`

- Exige sessão válida ou é idempotente para cookie ausente/expirado.
- Revoga a sessão atual com motivo `Logout` e expira o cookie com os mesmos atributos de caminho e segurança.
- Retorna `204 No Content`.

### `POST /api/v1/auth/activation`

Request:

```json
{
  "token": "token-de-uso-unico",
  "password": "nova-senha",
  "passwordConfirmation": "nova-senha"
}
```

Comportamento:

- valida token, finalidade, expiração, uso e revogação;
- valida política de senha e confirmação;
- cria ou atualiza `user_credentials`, altera estado para `Active`, consome o token e revoga sessões anteriores na mesma transação;
- retorna `204 No Content`; o usuário faz login em seguida.

Quando a finalidade do token for `InvitationAcceptance`, a mesma operação também deve validar a membership `Invited` associada ao convite e movê-la para `Active`. O token não pode ativar membership de outro tenant ou outro usuário.

### `POST /api/v1/invitations`

Exige administrador autorizado. A rota pertence ao domínio de identidade e pode permanecer sob `/api/v1/invitations` para representar convites de tenant.

Request:

```json
{
  "email": "novo.usuario@empresa.exemplo",
  "displayName": "Novo usuário",
  "role": "Professional",
  "clinicUnitIds": [
    "3fa85f64-5717-4562-b3fc-2c963f66afa6"
  ]
}
```

Comportamento:

- deriva o tenant da sessão e valida a capacidade para convidar;
- valida papel, unidades e limites do plano no servidor;
- cria ou localiza `User` pelo e-mail normalizado sem informar se ele já pertence a outro tenant;
- cria ou atualiza a membership somente para o tenant atual em estado `Invited`;
- cria token de `InvitationAcceptance`, revogando convites ativos anteriores da mesma membership;
- retorna `202 Accepted` sem expor token, memberships externas ou dados de outras empresas.

### `POST /api/v1/invitations/{invitationId}/acceptance`

Pode ser implementada como rota dedicada ou por `POST /api/v1/auth/activation`, mas a escolha deve ser única no OpenAPI. A proposta preferida é reutilizar `POST /api/v1/auth/activation` e distinguir a finalidade pelo token, evitando dois fluxos diferentes de definição de senha.

### `POST /api/v1/auth/password-recovery-requests`

Request:

```json
{
  "email": "admin@empresa.exemplo"
}
```

Retorna sempre `202 Accepted` com mensagem neutra. Se a conta for elegível, cria token de recuperação e entrega à interface de comunicação; nunca retorna token no JSON, log ou mensagem de erro.

### `POST /api/v1/auth/password-recovery-confirmations`

Possui o mesmo formato e validações da ativação, mas exige token com finalidade `PasswordRecovery`. Em sucesso, revoga todas as sessões existentes e retorna `204 No Content`.

## Validações

### E-mail

- Normalizar para `Trim().ToUpperInvariant()` apenas para busca e unicidade; preservar o valor apresentado em formato normalizado pela aplicação.
- Limite máximo de 320 caracteres.
- Não aceitar espaço inicial/final após a normalização.
- A validação de formato reduz entradas inválidas, mas não verifica entrega de e-mail no login.

### Senha

Política inicial proposta:

- mínimo de 12 caracteres;
- máximo de 128 caracteres;
- não registrar, retornar, reutilizar em URL ou incluir em exceções;
- bloquear senhas presentes em lista de senhas comprometidas quando houver serviço aprovado;
- exigir letras e números; a exigência adicional de símbolo é decisão de segurança a confirmar;
- comparar confirmação em tempo constante quando aplicável.

### Token e sessão

- Token de ativação/recuperação: Base64Url, tamanho mínimo equivalente a 32 bytes aleatórios.
- Cookie de sessão malformado, sessão desconhecida, segredo incorreto, revogada ou expirada: resposta `401` genérica e cookie expirado.
- Comparar hashes com comparação de tempo constante.

## Autorização após autenticação

Implementar um `CurrentPrincipal` ou serviço equivalente, derivado exclusivamente da sessão persistida. Ele fornece:

- `UserId`;
- `SessionId`;
- `ActiveTenantId`, quando selecionado;
- papel global de plataforma, se existir e estiver ativo;
- memberships ativas do tenant selecionado;
- unidades autorizadas pela membership;
- dados suficientes para auditoria, sem carregar conteúdo clínico.

O `CurrentPrincipal` não autoriza por si só uma operação clínica. Casos de uso ainda verificam a capacidade necessária, unidade e vínculo de cuidado. Não implementar filtro global que apenas confie em `active_tenant_id` sem verificar membership a cada operação relevante.

## Proteções HTTP

### CSRF

Como a sessão é baseada em cookie, ações inseguras (`POST`, `PUT`, `PATCH`, `DELETE`) precisam de proteção CSRF.

- Configurar antiforgery do ASP.NET Core com header `X-CSRF-Token`.
- Fornecer token para o frontend por endpoint seguro ou padrão de cookie não-HttpOnly dedicado ao antiforgery, sem confundir esse token com autenticação.
- Validar `Origin` para requisições inseguras quando o header estiver presente.
- Login, ativação e recuperação também devem validar origem e rate limit.
- `GET`, `HEAD` e `OPTIONS` não podem alterar estado.

### Rate limiting

Aplicar limites por ambiente e usar resposta `429` com Problem Details e `Retry-After` quando aplicável.

Proposta inicial:

| Rota | Limite inicial |
| --- | --- |
| Login | 10 tentativas por IP em 10 minutos e 5 por e-mail normalizado em 15 minutos |
| Ativação e confirmação de recuperação | 10 tentativas por IP em 15 minutos |
| Solicitação de recuperação | 3 solicitações por e-mail em 30 minutos e 10 por IP em 30 minutos |

Os limites são uma defesa adicional; não substituem bloqueio de conta, hash adequado ou monitoramento.

### CORS, headers e logs

- CORS usa origens configuradas por ambiente e métodos/headers mínimos necessários.
- Quando cookies forem usados entre frontend e API, habilitar `AllowCredentials` somente para as origens explicitamente configuradas.
- Configurar headers de segurança na borda e HTTPS obrigatório em produção.
- Não registrar `Cookie`, `Authorization`, senha, token de ação, valor de sessão ou corpo de login.
- Registrar evento de auditoria para login bem-sucedido, logout, troca de senha, ativação, bloqueio e alteração de administrador global. O evento não contém segredo ou conteúdo clínico.

## Bootstrap do primeiro superadministrador

O comando já existente cria `users` e `platform_administrators`, mas não cria senha. Completar o fluxo da seguinte forma:

1. Operador autorizado executa o bootstrap com e-mail e nome de exibição via variáveis de ambiente.
2. O caso de uso cria a identidade global e `platform_administrators` em estado ativo, sem membership de tenant.
3. O operador emite um token de `AccountActivation` por comando administrativo protegido ou fluxo de convite interno.
4. O token é entregue por canal seguro; não é exibido em logs, Swagger ou resposta HTTP comum.
5. O superadministrador define a primeira senha em `POST /auth/activation` e então pode usar `POST /auth/login`.

O comando é idempotente para administrador ativo com o mesmo usuário. Ele não reativa administrador inativo, não troca senha e não modifica dados de clínica.

## Primeiro administrador após o bootstrap

O bootstrap cria somente o operador global da Brothers Digital. Para tornar uma empresa utilizável, a próxima operação administrativa precisa criar o tenant e convidar seu primeiro `ClinicAdministrator`; criar o tenant sem esse convite não deve conceder acesso a nenhuma pessoa da clínica.

## Organização sugerida

```text
src/
├── BrothersCare.Domain/
│   └── Identity/
│       ├── UserCredential.cs
│       ├── UserSession.cs
│       └── AccountActionToken.cs
├── BrothersCare.Application/
│   └── Identity/
│       ├── Login/
│       ├── Session/
│       ├── Activation/
│       ├── PasswordRecovery/
│       └── Contracts/
├── BrothersCare.Infrastructure/
│   └── Identity/
│       ├── PasswordHasher.cs
│       ├── SessionTokenGenerator.cs
│       ├── AuthenticationService.cs
│       └── Persistence/
└── BrothersCare.Api/
    └── Identity/
        ├── AuthEndpoints.cs
        ├── CurrentPrincipal.cs
        └── AntiforgeryConfiguration.cs
```

Manter endpoints finos. Regras de estado, expiração, revogação e seleção de tenant pertencem a casos de uso na Application e entidades do Domain. Persistência, hash e geração criptográfica de tokens ficam em Infrastructure.

## Sequência de implementação

1. Criar entidades, mappings EF Core, índices e migração das credenciais, sessões, tokens de ação e novos estados de membership.
2. Adicionar abstrações de hash de senha, geração de segredo, relógio e envio de mensagens somente quando houver consumidor concreto.
3. Implementar criação de tenant e convite do primeiro administrador, incluindo criação ou reutilização segura de `User`.
4. Implementar ativação de conta, aceite de convite e provisionamento seguro do primeiro superadministrador.
5. Implementar login, emissão de cookie opaco e consulta de sessão.
6. Implementar tenant ativo e middleware/handler de autenticação.
7. Implementar logout, revogação global em troca de senha e expiração de sessão.
8. Implementar recuperação de senha, rate limit, CSRF e CORS com credenciais.
9. Publicar OpenAPI, integrar o frontend e remover sessão mockada do `localStorage`.

## Testes obrigatórios

### Domínio e aplicação

- senha correta autentica; senha incorreta nunca diferencia usuário existente de inexistente;
- bloqueio ocorre após o limite configurado e expira conforme regra;
- token é de uso único, expira e tokens anteriores são revogados ao emitir novo;
- senha redefinida revoga sessões existentes;
- sessão não pode ultrapassar expiração absoluta;
- tenant ativo só é selecionado com membership ativa;
- convite para pessoa existente em outro tenant cria somente nova membership e não duplica `User`;
- membership convidada, suspensa ou revogada não pode ser usada para selecionar tenant ou consultar dados;
- superadministrador sem membership não ganha acesso a recurso clínico.

### Integração HTTP

- login emite cookie `HttpOnly`, `Secure` em produção e não retorna segredo em JSON;
- `GET /auth/session` funciona com cookie válido e retorna `401` para sessão expirada, revogada ou adulterada;
- logout invalida a sessão imediatamente e expira o cookie;
- requisições inseguras sem antiforgery válido são bloqueadas;
- CORS aceita apenas origens permitidas e não combina credenciais com origem curinga;
- recuperação retorna a mesma resposta para conta existente e inexistente;
- endpoints clínicos retornam `404` quando um ID pertence a outro tenant;
- Problem Details segue o contrato de tipo, status, `traceId` e `errors` quando aplicável.

### Segurança operacional

- testes não usam credenciais reais, e-mails reais, tokens reais nem conteúdo clínico;
- logs de integração são inspecionados para garantir ausência de senha e token;
- migrações são aplicadas em banco descartável antes de homologação;
- executar teste de concorrência para duas tentativas de consumo do mesmo token e dois logins simultâneos próximos ao bloqueio.

## Critérios de aceite

- Login, consulta de sessão, seleção de tenant, logout, expiração e recuperação funcionam sem `localStorage` para credenciais ou sessão.
- O primeiro superadministrador pode ativar a conta por token único e entrar sem receber tenant ou acesso clínico automático.
- Superadministrador consegue criar uma empresa e convidar seu primeiro administrador, sem obter acesso clínico à empresa criada.
- Administrador de clínica consegue convidar usuários somente para seu tenant e unidades autorizadas; o aceite ativa a membership correspondente.
- Sessões são revogadas no servidor e cookies roubados/alterados não são aceitos após logout, expiração ou troca de senha.
- Cada rota protegida usa identidade server-side; nenhum tenant é aceito apenas por payload do cliente.
- Problem Details, OpenAPI, validação, rate limiting e testes negativos estão implementados.
- Não há segredos, senhas, tokens ou dados clínicos reais em repositório, logs ou respostas HTTP.
