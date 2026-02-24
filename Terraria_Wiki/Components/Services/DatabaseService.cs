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
    private bool _initialized = false; // 标记是否已经初始化过
    public string DatabasePath { get; private set; }
    public DbMode Mode { get; private set; }

    public DatabaseService(string dbPath, DbMode mode)
    {
        DatabasePath = dbPath;
        Mode = mode;

        // 核心配置：
        // ReadWrite | Create: 允许读写和创建
        // SharedCache: 允许并发访问（比如边看边通过API更新）
        var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;

        _db = new SQLiteAsyncConnection(DatabasePath, flags);
    }

    // 1. 初始化：这里必须列出所有需要用到的表
    public async Task Init()
    {
        if (_initialized) return; // 如果已经初始化过，直接跳过
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
        }

        _initialized = true;
    }
    private async Task SeedWikiBooksAsync()
    {
        // 第一步：先数数表里有几条数据
        var count = await _db.Table<WikiBook>().CountAsync();

        // 第二步：如果表是空的 (count == 0)，说明用户是第一次打开 App
        if (count == 0)
        {
            // 准备你的默认数据
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

            // 第三步：一股脑存进去
            await _db.InsertAllAsync(defaultWikiBooks);
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

}

public class ManagerDbService : DatabaseService
{
    public ManagerDbService(string dbPath) : base(dbPath, DbMode.Manager)
    {
    }
}

// 2. 专门用于内容库的类型
public class ContentDbService : DatabaseService
{
    // 注意：这里默认连接 Terraria，后面可以通过方法切换
    public ContentDbService(string dbPath) : base(dbPath, DbMode.Content)
    {
    }
}