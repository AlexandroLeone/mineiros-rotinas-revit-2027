# BIM Rotinas — Revit 2027

## Interface 1.1 — baseada na imagem aprovada

O painel abre com as dez etapas à esquerda, prévia grande no centro, miniaturas abaixo, cotas/legendas/localização à direita e os botões de emissão no rodapé. A aba do Revit continua chamada **BIM Rotinas**. **Abrir BIM Copiloto** permanece disponível.

- A prévia e as seis miniaturas usam vistas reais do documento: planta baixa, cortes, fachada, cobertura, localização e quadros/planilhas. Uma miniatura representa uma vista do grupo; use a lista para escolher as demais vistas.
- Clique nas miniaturas ou selecione uma vista para mudar a prévia. Use a roda do mouse, os botões +/− ou arraste a imagem. **Ajustar** restaura o enquadramento. **Abrir no Revit** ativa a vista original.
- As prévias são exportadas com fundo branco; a preferência de cor do Revit é restaurada ao terminar, inclusive em caso de erro. A primeira abertura pode levar alguns segundos para gerar as miniaturas.
- Os formulários e listas de propostas abrem sobre a área central. **Voltar à prévia** retorna ao painel principal sem remover as vistas do modelo.
- **Recuos do lote** exige linhas de propriedade retas com referências nativas utilizáveis. Sem elas, a varredura gera um item de revisão manual, não uma medida presumida. A distância proposta não certifica o recuo legal.
- A prévia de localização mostra a vista existente. O programa não importa o DWG só por abrir o painel; clique em **Carregar DWG**, confirme a unidade e prossiga.

Quando uma DLL anterior já está carregada, a atualização do arquivo de instalação passa a valer **na próxima inicialização do Revit**. Salve seus projetos e reinicie o Revit quando for conveniente. Não é necessário reinstalar os outros plugins.

Módulo independente de documentação. Não substitui nem altera BIM Copiloto, Biblioteca de Famílias ou RvtMcp. A aba nova **BIM Rotinas** contém **Rotinas de Projeto**, **Localização DWG** e **Abrir BIM Copiloto**. A janela de Rotinas também tem o botão **Abrir BIM Copiloto ↗**, como solicitado.

## Primeiro uso

1. Crie um **novo projeto a partir do modelo** e salve-o como RVT do cliente. Não cadastre cada cliente diretamente no arquivo-mestre RTE.
2. Abra **BIM Rotinas → Rotinas de Projeto** e preencha cliente, código do projeto, endereço, responsável e pasta-base. Clique em **Salvar cadastro no modelo**.
3. Execute as etapas desejadas na lateral. Nenhuma rotina de desenho é executada só por abrir o painel.
4. Salve o projeto pelo Revit depois das alterações, revisões e emissões. O módulo não salva nem fecha seu modelo automaticamente.

## Rotinas

| Etapa | Funcionamento |
|---|---|
| Cadastro | Guarda dados no projeto e atualiza os parâmetros nativos Cliente, Nome, Número e Endereço. Carimbos só refletem os parâmetros aos quais suas famílias estão vinculadas. |
| Cortes A/B/C | Selecione a planta; marque dois pontos por corte ou gere três posições iniciais a partir do volume visível. Esc cancela a seleção manual antes da criação. Cortes deste módulo já existentes são preservados. |
| Fachadas | Gera quatro elevações externas olhando para a edificação. Revise posição, sentido, alturas e nomes. |
| Pranchas | Escolha as vistas, o carimbo e uma folha existente ou folhas novas, uma por vista. Preserva vistas já posicionadas; tenta encaixar novas vistas sem mudar a escala. Considera caixas e títulos de outras vistas e elementos da folha; a composição final é revisada pelo profissional. |
| Checklist | Verifica cadastro, ambientes, cortes, fachadas, cobertura modelada, localização recortada, pranchas, carimbos e avisos. Lista os aspectos que exigem conferência manual. |
| Cotas | Escolha planta, corte ou fachada; varra para propor cotas entre faces nativas e referências de esquadrias. A simulação é revertida; só aplica as propostas selecionadas e validadas. Detecta duplicações e recusa varredura desatualizada. |
| Legendas | Propõe identificadores de ambientes, portas/janelas e materiais usando famílias já carregadas. Não cria uma vista de legenda gráfica/tabela de símbolos. Não inventa conteúdo de famílias nem baixa arquivos. |
| Revisão | Registra nome informado pelo profissional, data, pranchas e estado do modelo. Não é assinatura digital nem valida credenciais profissionais. |
| PDF | Combina as pranchas marcadas em um único PDF, com pasta exclusiva por emissão. Só emite após registrar a revisão para a mesma seleção e o mesmo estado do modelo. |
| Pastas | Cria a estrutura Cliente/Código. A emissão cria as pastas necessárias, mesmo que a etapa de pastas ainda não tenha sido executada. |
| Protocolo | Após protocolar por seus meios, informe número/data e selecione a emissão. Guarda uma cópia do PDF, seu SHA-256 e os dados do protocolo. Não transmite documentos à Prefeitura. |

### Localização simples com DWG

1. Na aba **Localização DWG**, selecione o arquivo e confirme a unidade correta. O caminho inicial é o mapa urbano indicado pelo usuário.
2. Escolha a planta para o nível de referência e a escala. Clique **1 · Carregar mapa no projeto**.
3. Selecione a vista LOC e clique **2 · Marcar retângulo no Revit**. O painel fica oculto enquanto você arrasta o retângulo no mapa.
4. Clique **3 · Selecionar localização nas pranchas** e escolha o carimbo/destino.

O mapa é importado como geometria de modelo para permitir recorte efetivo no Revit. Fica oculto nas demais vistas existentes; **vistas novas criadas fora deste módulo precisam ser conferidas**. O recorte é visual, não elimina geometria do DWG e não reduz o arquivo original. O programa não procura endereço automaticamente, não consulta cadastros municipais e não calcula norte geográfico. Confira escala, unidade, orientação e identificação do lote. Para reposicionar o recorte, abra a vista de localização, desative temporariamente Recortar vista no Revit e marque um novo retângulo.

### Cotas e identificações: limites importantes

As cotas automáticas oferecem pontos de partida, não uma composição universal. Paredes curvas/inclinadas, recuos do lote, objetos vinculados, famílias sem referências e sobreposições precisam de tratamento manual. Cadeias longas usam até 100 referências por eixo; material usa até 200 elementos por varredura. A existência de uma referência válida não certifica a correção técnica ou legibilidade da anotação. Inspecione o resultado na vista.

A planta de cobertura deve existir no modelo e ser selecionada na etapa de pranchas. O módulo não modela telhado, não cria a arquitetura do cliente e não substitui tabelas municipais por dados presumidos.

### Revisão e arquivos

Mudanças no modelo, alteração da seleção de folhas ou reabertura do projeto exigem nova revisão. A última revisão permanece registrada, mas sua validade é conferida novamente ao emitir. Salvar o modelo também pode exigir renovar a revisão. Faça a revisão por último, antes de emitir.

Destino inicial:

`C:\Users\Digimax\Documents\Projeto Em Andamento\#Projetos\2026\Nome do cliente\Código do projeto`

Subpastas: `00_Cadastro`, `01_Modelo`, `02_Pranchas`, `03_PDF`, `04_Protocolo`, `05_Aprovado`. O RVT não é copiado automaticamente; salve-o em `01_Modelo` pelo Revit. Cada emissão em `03_PDF` tem PDF, `emissao.json` e `cadastro.json`. O histórico associado ao projeto só persiste no RVT/RTE quando você o salva; os registros externos já gravados permanecem na pasta.

Não reutilize um RVT de cliente anterior para outro cliente. Use um modelo limpo. Depois da primeira emissão, o módulo bloqueia a troca de nome/código para evitar misturar históricos. A cópia protocolada e os registros existentes não são sobrescritos.

## Aprovação municipal

Este módulo é de documentação e conferência básica; **não certifica atendimento ao Código de Obras de Mineiros nem garante aprovação**. Não há diagnóstico legal automático nesta versão. A consulta à legislação vigente, viabilidade, recuos, índices urbanísticos, acessibilidade, ventilação/iluminação, titularidade, ART/RRT e exigências de protocolo continuam sob revisão profissional. Não foram codificados limites municipais sem validação de fonte e regra aplicável ao imóvel.

## Instalação e manutenção

`Instalar.ps1` instala somente `MineirosRotinas.addin` e uma pasta versionada `MineirosRotinas` em `%APPDATA%\Autodesk\Revit\Addins\2027`. Confere os hashes dos arquivos estáticos dos outros plugins antes e depois. Não modifica opções de segurança, manifestos ou DLLs dos outros módulos.

A instalação contém código-fonte (`src`), testes (`tests`) e compilado (`dist`). Para recompilar, use .NET SDK 10, Revit 2027 instalado e `Compilar.ps1 -DotnetPath <caminho-do-dotnet.exe>`. Não é necessário SDK para utilizar o plugin.

Se a aba não estiver disponível na sessão atual, salve seus projetos e reinicie o Revit quando for conveniente. Caso o Revit apresente confirmação de carregamento de add-in, a decisão é do usuário; o instalador não contorna esse aviso.

Nesta máquina, o Windows restringiu a execução do arquivo `.ps1`. A instalação foi realizada por cópia direta da DLL e do manifesto, sem alterar essa política. `Instalacao-verificada.json` registra a instalação e os hashes dos 37 arquivos DLL/manifesto dos outros plugins que permaneceram idênticos.

O mapa municipal completo tem aproximadamente 17,8 MB. Sua primeira importação pode levar vários minutos e ocupa a interface do Revit; aguarde a barra de progresso. Não repita a importação enquanto ela estiver em andamento. Uma vez carregado, os ajustes seguintes usam o recorte da vista.

Para desativar apenas este módulo, feche o Revit e mova `MineirosRotinas.addin` para fora da pasta Addins/2027. As rotinas criadas no modelo permanecem como elementos nativos do Revit. O botão de conversa depende da interface da versão BIM Copiloto 2.1.2 instalada; não substitui nem reconfigura suas conexões.
