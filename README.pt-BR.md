# Marcelino Client (.NET)

[Português (Brasil)](README.pt-BR.md) | [English](README.md)

Projeto independente do cliente desktop para Windows usando .NET e WPF para o aplicativo Marcelino Client. Esta é apenas a parte do cliente; para a parte do servidor e funcionalidade completa, veja o projeto Python existente em [BackupLAN](https://github.com/marceloh220/BackupLAN). A versão do cliente em Python possui os mesmos recursos e funcionalidades que este cliente .NET, garantindo consistência entre as plataformas.


## Requisitos

- SDK do .NET 10
- Windows com o pacote de direcionamento do WPF para .NET 10

## Compilar e executar

A partir deste diretório:

```powershell
dotnet build .\MarcelinoClient.sln
dotnet run --project .\MarcelinoClient\MarcelinoClient.csproj
```

## Instalador e inicialização com o Windows

Gere um instalador autossuficiente para Windows x64. O computador de destino não precisa ter o .NET instalado:

```powershell
.\Installer\build-installer.ps1 -DownloadCompiler
```

O script baixa o pacote NuGet portátil `Tools.InnoSetup` na versão fixa 6.7.3, verifica seu SHA-512 com o catálogo NuGet, publica o cliente e cria `artifacts\installer\MarcelinoClient-Setup-1.0.0-win-x64.exe`. Como alternativa, use um compilador Inno Setup instalado com `-IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'`; informe `-Version 1.0.1` para gerar outra versão. Se o PowerShell bloquear scripts, permita a execução apenas no processo atual (`Set-ExecutionPolicy -Scope Process Bypass`), não para toda a máquina. O compilador do Inno Setup exibe os termos da licença; a versão 6.7.3 indica uso exclusivamente não comercial. Para uso ou distribuição comercial, obtenha a licença apropriada do compilador.

O instalador está em português do Brasil, não exige elevação e instala em `%LOCALAPPDATA%\Programs\MarcelinoClient`, com um atalho no menu Iniciar e um atalho opcional na área de trabalho. A opção **Iniciar com o Windows (na bandeja)** é opcional e vem desmarcada. Ela registra o caminho do executável entre aspas, com `--tray`, em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, para a entrada do usuário atual no Windows, não antes do login. A mesma opção **Iniciar com o Windows** pode ser alterada posteriormente no menu da bandeja. A inicialização não salva senhas, não autentica no NAS e não inicia transferências. Os aplicativos de inicialização do Windows ou o Gerenciador de Tarefas também podem desativar a execução automática.

Apenas uma instância é permitida por sessão do Windows. Uma execução normal restaura a instância existente; uma execução duplicada com `--tray` termina silenciosamente. A desinstalação remove os atalhos e o registro de inicialização automática, mas preserva as preferências e as impressões digitais SSH confiáveis em `%APPDATA%\marcelinoSrv`. Encerre o cliente pelo menu da bandeja antes de atualizar ou desinstalar. O instalador gerado não possui assinatura de código; versões de produção devem ser assinadas antes da distribuição. A compilação e as verificações não instalam nem desinstalam o aplicativo automaticamente.

## Escopo da portabilidade

O cliente WPF inclui descoberta e autenticação SMB, com abas separadas para Sistema, Discos, RAID, Internet, DNS/DHCP, Compartilhamentos, Backup e Terminal. Também oferece backup e restauração incrementais e um terminal SSH interativo.

## Idioma da interface

Use o seletor **Idioma**, no cabeçalho do aplicativo (**Language** na interface em inglês), para alternar entre português do Brasil (`pt-BR`) e inglês dos EUA (`en-US`). As opções aparecem com os nomes nativos dos idiomas. O português do Brasil é o padrão, inclusive quando o idioma salvo está ausente ou não é suportado.

A mudança é aplicada imediatamente, sem reiniciar o aplicativo nem reconectar ao NAS. Os rótulos da interface, os cabeçalhos das tabelas, as mensagens de estado do SSH e os registros de backup gerados pelo aplicativo usam o idioma selecionado. Textos recebidos do servidor, como a saída do terminal SSH, não são traduzidos. O instalador permanece em português do Brasil, independentemente do idioma do cliente.

A seleção é salva imediatamente no campo `language` de `%APPDATA%\marcelinoSrv\settings.json` e restaurada na próxima execução. As traduções estão nos dicionários de [português do Brasil](MarcelinoClient/Localization/Strings.pt-BR.xaml) e [inglês dos EUA](MarcelinoClient/Localization/Strings.en-US.xaml); ambos devem conter as mesmas chaves de recursos.

## Backup e restauração

A aba Backup permite navegar pelos compartilhamentos SMB visíveis do servidor autenticado e por suas subpastas. Clique duas vezes em um diretório para abri-lo, use o botão de subir para voltar e selecione ou abra um diretório antes de escolher **Usar como destino**. Adicione as pastas locais de origem, escolha **Iniciar backup** e confirme o destino. Cada origem é copiada para uma subpasta com seu nome original, incluindo diretórios vazios. Origens com nomes de pasta duplicados e caminhos de origem e destino sobrepostos são rejeitados.

Selecione um arquivo ou uma pasta remota, ou deixe um diretório aberto, escolha **Restaurar para o PC...**, selecione uma pasta local e confirme o caminho final. Os diretórios são restaurados para uma subpasta com o nome da pasta remota selecionada. A opção **Restauração incremental** vem habilitada.

A comparação incremental segue o cliente Python: tamanho de arquivo igual e data de modificação do destino não anterior à data da origem menos dois segundos. Não há comparação por soma de verificação. As transferências preservam as datas de modificação e acesso, usam até quatro tarefas simultâneas e nunca excluem arquivos do destino que não existam na origem. Links simbólicos e pontos de nova análise do Windows não são seguidos; itens ignorados e erros são informados no registro.

O progresso e os erros aparecem na aba. **Cancelar** solicita o cancelamento; operações SMB já bloqueadas no Windows podem demorar para retornar. Os arquivos são copiados para arquivos temporários no mesmo diretório e renomeados somente após a cópia completa. Assim, um cancelamento ou uma falha de cópia não trunca o destino anterior. Arquivos já concluídos permanecem após o cancelamento. Desconectar ou encerrar o aplicativo cancela as transferências e ignora retornos da sessão anterior. As seleções de backup são salvas localmente com as demais preferências.

## Bandeja do sistema

A barra de título nativa é substituída pelo cabeçalho do aplicativo, com controles de minimizar, maximizar/restaurar e fechar à direita. Arraste a área não interativa do cabeçalho para mover a janela; clique duas vezes para maximizar ou restaurar. O redimensionamento continua disponível nas bordas da janela. Fechar continua enviando o aplicativo para a bandeja; use **Sair** para encerrá-lo. O executável, a janela e a bandeja compartilham o mesmo ícone de NAS em múltiplas resoluções, também usado nos atalhos do aplicativo.

O aplicativo cria um ícone de NAS na área de notificação do Windows. Minimizar ou fechar a janela a oculta na bandeja sem encerrar a sessão autenticada, o monitoramento, o SSH ou as transferências ativas. Clique duas vezes no ícone ou escolha **Restaurar** para exibir a janela novamente. **Desconectar**, no aplicativo ou no menu da bandeja, encerra a sessão do servidor, fecha o SSH, limpa os dados da sessão e exibe a tela de login. Escolha **Sair** para encerrar o aplicativo, cancelar transferências, fechar SSH/SMB e remover o ícone. Se a inicialização da bandeja falhar, o fechamento normal permanece disponível. O Windows pode colocar o ícone no menu de ícones ocultos da área de notificação.

## Preferências

As preferências são armazenadas em `%APPDATA%\marcelinoSrv\settings.json`, usando os nomes de campos do cliente Python. O cliente restaura servidor, usuário/domínio, pastas locais de backup, destino de backup, caminho da chave privada SSH, tema claro/escuro e [idioma da interface](#idioma-da-interface). Senhas, frases secretas SSH e o conteúdo de chaves privadas nunca são incluídos nas preferências.

As mudanças de tema e seleção são salvas imediatamente. Os campos de servidor, usuário, domínio e caminho da chave editados manualmente são salvos ao perderem o foco, antes de conectar ou desconectar e ao fechar a janela. Desconectar limpa a sessão ativa, mas mantém as preferências. O destino de backup é restaurado após a autenticação apenas quando o servidor salvo corresponde ao servidor conectado; caminhos de outro servidor não são reutilizados. Nenhuma conexão ou transferência é iniciada automaticamente ao abrir o aplicativo.

A gravação usa um arquivo temporário no mesmo diretório e uma renomeação atômica, preservando campos JSON não relacionados usados pelo cliente Python. Na ausência de preferências, os valores padrão são usados. Um arquivo de preferências inválido ou ilegível é preservado e um erro é exibido, em vez de sobrescrevê-lo. Corrija ou renomeie esse arquivo e reinicie o aplicativo para reativar o salvamento das preferências.

## Monitoramento do NAS

Após a autenticação SMB, o cliente lê `\\servidor\Monitor\.marcelinoSrv.json` usando a sessão SMB autenticada do Windows. O usuário precisa ter permissão de leitura no compartilhamento oculto `Monitor`, e o agente do NAS deve publicar o mesmo formato JSON usado pelo cliente Python. Nenhuma alteração no NAS é necessária.

As abas de monitoramento exibem CPU, memória, tempo de atividade, contadores de bytes de rede, hardware, sistemas de arquivos, discos físicos/SMART, RAID, histórico de testes de velocidade da Internet, eventos e concessões DNS/DHCP e permissões Samba. `Monitor` permanece oculto no navegador de compartilhamentos. Após a leitura dos metadados do agente, os compartilhamentos configurados com `browseable = no`, também `false` ou `0`, ficam ocultos tanto no navegador quanto na tabela de permissões. A visibilidade é reaplicada a cada atualização bem-sucedida, sem remover compartilhamentos SMB cuja configuração não seja publicada pelo agente.

A primeira leitura começa após o login. A atualização automática usa um intervalo padrão de cinco segundos; a aba Sistema permite selecionar o intervalo e atualizar manualmente. As leituras são executadas fora da tarefa da interface, sem sobreposição. Desconectar ou encerrar o aplicativo interrompe a consulta periódica, limpa as métricas e descarta resultados atrasados. Se o arquivo de estado não puder ser acessado ou contiver JSON inválido, o cliente desconecta automaticamente, interrompe as consultas, fecha o SSH, limpa os dados da sessão e retorna à tela de login. A mensagem de estado do login informa o motivo da desconexão; conecte novamente após resolver a causa.

Os serviços Python e os scripts de configuração executados no NAS permanecem fora deste projeto.

## Terminal SSH

Após o login SMB, a aba Terminal conecta ao mesmo servidor e usuário por SSH na porta 22. Deixe o campo de chave privada vazio para usar a senha do login, ou selecione uma chave privada e informe sua frase secreta opcional. O SSH.NET cuida da autenticação e criptografia; o VtNetCore interpreta o terminal interativo `xterm-256color`, incluindo cores ANSI, movimentação do cursor e redimensionamento do terminal.

Na primeira conexão, confira a impressão digital SHA-256 exibida com o administrador do servidor antes de aceitar. As chaves aceitas são fixadas em `%APPDATA%\marcelinoSrv\ssh-host-keys.json`, independentemente do arquivo `known_hosts` do OpenSSH. Chaves alteradas são rejeitadas, e um arquivo de confiança ilegível ou corrompido impede a conexão. Remova uma chave fixada desatualizada somente após verificar, por um meio independente, que a troca da chave do servidor é legítima. Senhas e frases secretas não são salvas em disco.

O botão SSH conecta, cancela uma conexão pendente ou desconecta um terminal ativo. Desconectar do SMB ou encerrar o aplicativo também fecha o SSH e limpa as credenciais de login mantidas em memória. O terminal integrado aceita entrada normal do teclado e teclas de controle; Ctrl+Shift+C copia a tela visível, e Ctrl+Shift+V ou o botão de colar insere texto. Colagens com várias linhas exigem confirmação. O envio de eventos do mouse e a navegação pelo histórico de rolagem não estão implementados nesta versão inicial do terminal.

## Verificações locais

Execute no Windows as verificações de JSON e análise de dados, backup e restauração locais, confiança e cancelamento SSH, emulação de terminal, localização e vinculação de dados nas abas WPF:

```powershell
dotnet run --project .\Checks\MarcelinoClient.Checks.csproj
```

As verificações de localização cobrem a correspondência entre as chaves das traduções, a troca imediata de idioma e sua persistência, os cabeçalhos traduzidos das tabelas e a atualização das mensagens de estado do SSH e dos registros de backup.

As verificações transferem arquivos apenas dentro de diretórios locais temporários e abrem brevemente uma janela WPF com dados simulados, fechando-a em seguida. Elas não conectam a um NAS. Permissões SMB reais, backup e restauração com Samba, autenticação SSH e comportamento do terminal interativo, além da saída do agente do NAS, ainda devem ser verificados no ambiente de destino.