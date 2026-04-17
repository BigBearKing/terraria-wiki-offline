using SQLite;
using Terraria_Wiki.Models;
namespace Terraria_Wiki.Services;

public enum DbMode
{
    Manager, // 仅用于管理 (Manager.db)
    Content  // 具体内容库 (Terraria.db 或 Calamity.db)
}
public class DatabaseService
{
    private SQLiteAsyncConnection _db;
    public bool _initialized = false; // 标记是否已经初始化过
    public string DatabasePath { get; private set; }
    public DbMode Mode { get; private set; }

    public DatabaseService(string dbPath, DbMode mode)
    {
        DatabasePath = dbPath;
        Mode = mode;

        var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;
        string path = Path.GetDirectoryName(DatabasePath);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        _db = new SQLiteAsyncConnection(DatabasePath, flags);
    }

    // 1. 初始化：这里必须列出所有需要用到的表
    public async Task Init(bool force = false)
    {
        if (_initialized && !force) return; // 如果已经初始化过且不强制，直接跳过
        await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;");
        // 在这里把所有表都建好


        if (Mode == DbMode.Manager)
        {
            await _db.CreateTableAsync<WikiBook>();
            await SeedWikiBooksAsync();

        }
        else
        {

            await _db.CreateTableAsync<WikiPage>();
            await _db.CreateTableAsync<WikiRedirect>();
            await _db.CreateTableAsync<WikiHistory>();
            await _db.CreateTableAsync<WikiFavorite>();
            await _db.CreateTableAsync<WikiAsset>();
            await InitFtsTableAsync();
            await SeedWikiPageAsync();
        }

        _initialized = true;
    }
    public async Task InitFtsTableAsync()
    {
        string createSql = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS WikiSearchIndex USING fts5(
                Title UNINDEXED,          -- 加上 UNINDEXED，现在标题也不参与搜索了
                PlainContent,             -- 只有纯文本内容参与搜索
                tokenize='trigram'        -- 开启支持中文的分词器
            );";

        // 直接执行建表语句
        await _db.ExecuteAsync(createSql);
    }

    public SQLiteAsyncConnection GetConnection()
    {
        return _db; // 返回你类内部持有的异步连接对象
    }

    public async Task CloseConnection()
    {
        await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode = DELETE;");
        await _db.CloseAsync();
    }
    public async Task ReconnectAsync()
    {
        // 1. 重置初始化状态
        _initialized = false;

        // 2. 安全地关闭并释放旧连接（最关键的一步）
        if (_db != null)
        {
            await _db.CloseAsync(); // 确保底层文件锁被释放
            _db = null;
        }

        var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;
        string path = Path.GetDirectoryName(DatabasePath);

        // 3. 确保目录存在 (加了 null 检查更严谨)
        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        // 4. 建立新连接
        _db = new SQLiteAsyncConnection(DatabasePath, flags);

        await Init();
    }
    public async Task DeleteDatabaseFile()
    {
        File.Delete(DatabasePath);
    }

    private async Task SeedWikiBooksAsync()
    {
        // 第一步：准备好你所有的“默认数据”清单
        var defaultWikiBooks = new List<WikiBook>
    {
        new WikiBook
        {
            Id=1,
            Title = "Terraria Wiki",
            Description = "《泰拉瑞亚》是冒险之地！是神秘之地！是可让你塑造、捍卫、享受的大地。在泰拉瑞亚，你有无穷选择。手指发痒的动作游戏迷？建筑大师？收藏家？探险家？每个人都能找到自己想要的。",
            IsPageDownloaded = false,
            IsResourceDownloaded = false,
        },
        new WikiBook
        {
            Id=2,
            Title = "Calamity Wiki",
            Description = "灾厄模组是泰拉瑞亚的最大内容添加类模组，在原版毕业之后加入了数个小时的新流程，还有大量新敌怪和数量超越原版的新Boss。",
            IsPageDownloaded = false,
            IsResourceDownloaded = false,
        }
    };

        // 第二步：查询当前数据库里已经存在的数据
        var existingBooks = await _db.Table<WikiBook>().ToListAsync();
        // 提取出所有已经存在的 Id，存进 HashSet 方便快速查找
        var existingIds = existingBooks.Select(b => b.Id).ToHashSet();

        // 第三步：比对并筛选出“缺失”的数据
        // 遍历默认数据，如果它的 Id 不在 existingIds 里，说明缺失了
        var missingBooks = defaultWikiBooks.Where(b => !existingIds.Contains(b.Id)).ToList();

        // 第四步：如果发现有缺失的，把缺失的部分统一存进去
        if (missingBooks.Any())
        {
            await _db.InsertAllAsync(missingBooks);
        }
    }

    private async Task SeedWikiPageAsync()
    {
        var count = await _db.Table<WikiPage>().CountAsync();
        if (count == 0)
        {
            var defaultPage = new WikiPage();
            defaultPage.Title = "Terraria Wiki";
            defaultPage.Content = "请先下载数据";
            defaultPage.LastModified = DateTime.Now;
            await _db.InsertAsync(defaultPage);
        }

    }
    // 2.1 通用功能：更新或插入 (InsertOrReplace) - 推荐用这个
    // 如果主键存在就更新，不存在就插入
    public async Task SaveItemAsync<T>(T item) where T : new()
    {
        await Init();
        await _db.InsertOrReplaceAsync(item);
    }

    public async Task SaveItemsAsync<T>(IEnumerable<T> items) where T : new()
    {
        await Init();
        foreach (var item in items)
        {
            await _db.InsertOrReplaceAsync(item);
        }
    }

    public async Task<int> GetCountAsync<T>() where T : new()
    {
        await Init();
        return await _db.Table<T>().CountAsync();
    }

    // 2.2 通用功能：删除
    public async Task DeleteItemAsync<T>(object primaryKey) where T : new()
    {

        await Init();
        T item = await _db.FindAsync<T>(primaryKey);
        await _db.DeleteAsync(item);
    }

    public async Task DeleteItemsAsync<T>() where T : new()
    {

        await Init();
        await _db.DeleteAllAsync<T>();
    }

    // 3. 通用功能：取整张表的数据
    public async Task<List<T>> GetItemsAsync<T>() where T : new()
    {
        await Init();
        return await _db.Table<T>().ToListAsync();
    }

    // 4. 通用功能：根据主键取一条数据 (比如根据 Title 或 Id)
    public async Task<T> GetItemAsync<T>(object primaryKey) where T : new()
    {
        await Init();
        return await _db.FindAsync<T>(primaryKey);
    }

    //验证是否存在
    public async Task<bool> ItemExistsAsync<T>(object primaryKey) where T : new()
    {
        await Init();

        var mapping = _db.GetConnection().GetMapping(typeof(T));

        var query = $"SELECT COUNT(*) FROM \"{mapping.TableName}\" WHERE \"{mapping.PK.Name}\" = ?";

        var count = await _db.ExecuteScalarAsync<int>(query, primaryKey);
        return count > 0;
    }

    //验证历史
    public async Task SaveHistoryAsync(WikiHistory wikiHistory)
    {
        await Init();

        // 1. 根据你的业务逻辑（标题 + 日期）去查旧数据
        var existingItem = await _db.Table<WikiHistory>()
                                    .Where(x => x.WikiTitle == wikiHistory.WikiTitle
                                             && x.DateKey == wikiHistory.DateKey)
                                    .FirstOrDefaultAsync();

        if (existingItem != null)
        {
            // 2. 【关键步骤】如果存在，必须使用旧数据的 ID
            // 否则数据库不知道你要更新哪一行
            wikiHistory.Id = existingItem.Id;

            // 更新 (Update)
            await _db.UpdateAsync(wikiHistory);
        }
        else
        {
            // 3. 不存在，直接插入 (Insert)
            // 此时 Id 为 0，数据库会自动生成新 Id
            await _db.InsertAsync(wikiHistory);
        }
    }

    //获取页面标题和时间
    public async Task<List<WikiPageSummary>> GetPageSummariesPagedAsync(int startIndex, int count)
    {
        await Init();
        string sql = "SELECT Title, LastModified FROM WikiPage ORDER BY Title LIMIT ? OFFSET ?";
        return await _db.QueryAsync<WikiPageSummary>(sql, count, startIndex);
    }

    // 分页获取历史记录（按阅读时间倒序排）
    public async Task<List<WikiHistory>> GetWikiHistoryPagedAsync(int startIndex, int count)
    {
        await Init();
        // 使用 DESC 按时间倒序，确保最新看的在最前面
        string sql = "SELECT * FROM WikiHistory ORDER BY ReadAt DESC LIMIT ? OFFSET ?";
        return await _db.QueryAsync<WikiHistory>(sql, count, startIndex);
    }

    public async Task<List<WikiFavorite>> GetWikiFavoritePagedAsync(int startIndex, int count)
    {
        await Init();
        // 使用 DESC 按时间倒序，确保最新看的在最前面
        string sql = "SELECT * FROM WikiFavorite ORDER BY FavoritedAt DESC LIMIT ? OFFSET ?";
        return await _db.QueryAsync<WikiFavorite>(sql, count, startIndex);
    }

    //释放空间
    public async Task VacuumDatabaseAsync()
    {
        // 执行 SQLite 原生的 VACUUM 命令
        await _db.ExecuteAsync("VACUUM");
    }

    //储存搜索索引
    public async Task SaveSearchIndexAsync(string title, string plainContent)
    {
        // 1. 防空保护
        if (string.IsNullOrWhiteSpace(plainContent))
        {
            return;
        }

        // 开启事务保证原子性（要么都成功，要么都失败）
        await _db.RunInTransactionAsync(tran =>
        {
            // 2. 先根据 Title 删除旧的索引（如果不存在，这句也不会报错）
            tran.Execute("DELETE FROM WikiSearchIndex WHERE Title = ?", title);

            // 3. 再插入新的索引
            tran.Execute(@"
            INSERT INTO WikiSearchIndex (Title, PlainContent) 
            VALUES (?, ?)",
                title, plainContent);
        });
    }

    //搜索功能
    public async Task<List<SearchResultItem>> SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<SearchResultItem>();
        }
        int maxSearchItemCount = Preferences.Default.Get("MaxSearchItemCount", 50);
        // 1. 关键词清洗与预处理
        string cleanKeyword = keyword.Replace("\"", "").Replace("'", "").Trim();
        string likeTerm = $"%{cleanKeyword}%";

        var terms = cleanKeyword.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var ftsTerms = terms.Select(t => $"\"{t}\"*");
        string matchTerm = string.Join(" AND ", ftsTerms);

        // 2. 终极融合 SQL
        string sql = $@"
            WITH 
            -- 【步骤 1：第一梯队 A】先查标题表（极快）
            TitleMatches AS (
                SELECT Title, '' AS RedirectTo, 1 AS Priority, 0 AS RankScore
                FROM WikiPage 
                WHERE Title LIKE ?
                LIMIT {maxSearchItemCount}
            ),

            -- 【步骤 2：第一梯队 B】查重定向表（带短路判定）
            RedirectMatches AS (
                SELECT FromName AS Title, 
                       -- 智能去除 # 及后续锚点文字
                       CASE WHEN INSTR(ToTarget, '#') > 0 THEN SUBSTR(ToTarget, 1, INSTR(ToTarget, '#') - 1) ELSE ToTarget END AS RedirectTo, 
                       1 AS Priority, 0 AS RankScore
                FROM WikiRedirect 
                WHERE FromName LIKE ?
                  -- ⚔️ 核心短路逻辑：如果 TitleMatches 已经够 xx 个了，这句直接为 FALSE，整段查询瞬间跳过
                  AND (SELECT COUNT(*) FROM TitleMatches) < {maxSearchItemCount}
                LIMIT {maxSearchItemCount}
            ),

            -- 【步骤 3：合并第一梯队】把它俩拼起来，并做一次内部去重统计
            Tier1Pool AS (
                SELECT * FROM TitleMatches
                UNION ALL
                SELECT * FROM RedirectMatches
            ),
            Tier1Unique AS (
                SELECT Title, MIN(RedirectTo) AS RedirectTo, MIN(Priority) AS Priority, MIN(RankScore) AS RankScore
                FROM Tier1Pool
                GROUP BY Title
            ),

            -- 【步骤 4：第二梯队】FTS5 全文搜索（带短路判定）
            FtsMatches AS (
                SELECT Title, '' AS RedirectTo, 2 AS Priority, rank AS RankScore,
                       -- FTS 自己生成的高亮摘要先存起来
                       snippet(WikiSearchIndex, 1, '<mark class=""search-highlight"">', '</mark>', '...', 60) AS FtsSnippet
                FROM WikiSearchIndex 
                WHERE WikiSearchIndex MATCH ?
                  -- ⚔️ 核心短路逻辑：如果第一梯队已经凑满 xx 个词条，彻底封死 FTS5 的调用
                  AND (SELECT COUNT(*) FROM Tier1Unique) < {maxSearchItemCount}
                LIMIT 50
            ),

            -- 【步骤 5：大一统】把第一梯队和 FTS5 结果混装
            AllRawMatches AS (
                SELECT Title, RedirectTo, Priority, RankScore, '' AS FtsSnippet FROM Tier1Unique
                UNION ALL
                SELECT Title, RedirectTo, Priority, RankScore, FtsSnippet FROM FtsMatches
            ),

            -- 【步骤 6：全局去重与终极排序】
            FinalTop50 AS (
                SELECT 
                    Title, 
                    MIN(RedirectTo) AS RedirectTo, 
                    MIN(Priority) AS MinPriority,
                    MIN(RankScore) AS FinalRankScore,
                    MAX(FtsSnippet) AS FtsSnippet -- 把 FTS 的摘要带着
                FROM AllRawMatches
                GROUP BY Title
                ORDER BY 
                    MinPriority ASC, 
                    CASE MinPriority WHEN 1 THEN LENGTH(Title) ELSE MIN(RankScore) END ASC,
                    Title ASC
                LIMIT {maxSearchItemCount}
            )

            -- 【最终步：精准制导提取摘要】
            -- 只有这胜出的 xx 位天之骄子，才有资格去取摘要！
            SELECT 
                f.Title, 
                f.RedirectTo,
                CASE 
                    -- 如果是第一梯队（靠标题命中的），去原表切前 60 个字当摘要
                    WHEN f.MinPriority = 1 THEN 
                        COALESCE((
                            SELECT SUBSTR(PlainContent, 1, 60) || '...' 
                            FROM WikiSearchIndex 
                            -- 使用清理过 # 的标题去找正文
                            WHERE Title = CASE WHEN f.RedirectTo = '' THEN f.Title ELSE f.RedirectTo END 
                            LIMIT 1
                        ), '暂无描述...')
                    -- 如果是第二梯队，直接用刚才 FTS 自带的高亮摘要
                    ELSE f.FtsSnippet 
                END AS Snippet,
                f.MinPriority
            FROM FinalTop50 f
            ORDER BY 
                f.MinPriority ASC, 
                CASE f.MinPriority WHEN 1 THEN LENGTH(f.Title) ELSE f.FinalRankScore END ASC,
                f.Title ASC;
    ";

        // 传入参数 (对应 WikiPage, WikiRedirect, WikiSearchIndex 的三个问号)
        return await _db.QueryAsync<SearchResultItem>(sql, likeTerm, likeTerm, matchTerm);
    }

    // 1. 获取过滤后的总数
    public async Task<int> GetFilteredCountAsync(string tableName, string searchColumn, string keyword)
    {
        string likeTerm = $"%{keyword}%";
        string sql = $"SELECT COUNT(*) FROM {tableName} WHERE {searchColumn} LIKE ?";
        return await _db.ExecuteScalarAsync<int>(sql, likeTerm);
    }

    // 简易重载版：自动推断表名 (适用于直接查原表的情况)
    public async Task<int> GetFilteredCountAsync<T>(string searchColumn, string keyword) where T : new()
    {
        var tableName = typeof(T).Name;
        return await GetFilteredCountAsync(tableName, searchColumn, keyword);
    }

    // 2. 获取过滤后的分页摘要数据
    public async Task<List<T>> GetFilteredPagedAsync<T>(
        string tableName,
        string selectColumns,
        string searchColumn,
        string keyword,
        string orderByColumn,
        int startIndex,
        int count,
        bool isDesc = true) where T : new()
    {
        string likeTerm = $"%{keyword}%";
        string orderDir = isDesc ? "DESC" : "ASC";

        string sql = $@"
        SELECT {selectColumns} 
        FROM {tableName} 
        WHERE {searchColumn} LIKE ? 
        ORDER BY {orderByColumn} {orderDir} 
        LIMIT ? OFFSET ?";

        return await _db.QueryAsync<T>(sql, likeTerm, count, startIndex);
    }

    // ==========================================
    // 3. 通用：分页 & 过滤查询 (简化版：直接 SELECT * 全表映射)
    // ==========================================
    public async Task<List<T>> GetFilteredPagedAsync<T>(
        string searchColumn,
        string keyword,
        string orderByColumn,
        int startIndex,
        int count,
        bool isDesc = true) where T : new()
    {
        var tableName = typeof(T).Name;

        // 底层调用高级版方法
        return await GetFilteredPagedAsync<T>(
            tableName: tableName,
            selectColumns: "*",
            searchColumn: searchColumn,
            keyword: keyword,
            orderByColumn: orderByColumn,
            startIndex: startIndex,
            count: count,
            isDesc: isDesc);
    }

}


