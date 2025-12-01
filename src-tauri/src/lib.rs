use std::fs;
use tauri::{
    http::{header::{ACCESS_CONTROL_ALLOW_ORIGIN, CONTENT_TYPE}, Response, StatusCode},
    path::BaseDirectory,
    Manager,
};

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        
        // 注册自定义协议 "wiki://"
        .register_uri_scheme_protocol("wiki", |ctx, request| {
            // 1. 获取 App Handle 用于路径解析
            let app_handle = ctx.app_handle();
            
            // 2. 解析请求的 URL 路径
            let uri_path = request.uri().path();
            
            // 处理根路径，默认重定向到 index.html
            // 例如: wiki://localhost/ -> index.html
            let path_str = if uri_path == "/" {
                "index.html"
            } else {
                // 去掉开头的 "/"，例如 "/css/style.css" -> "css/style.css"
                uri_path.trim_start_matches('/')
            };

            // 3. URL 解码 (处理文件名中的空格 %20 等特殊符号)
            let decoded_path = match urlencoding::decode(path_str) {
                Ok(path) => path.to_string(),
                Err(_) => path_str.to_string(),
            };

            // 4. 安全地解析资源路径
            // 这会查找 "wiki-assets/<decoded_path>" (开发环境)
            // 或安装目录下的 "resources/wiki-assets/<decoded_path>" (生产环境)
            let resource_path_result = app_handle.path().resolve(
                format!("../wiki-assets/{}", decoded_path), 
                BaseDirectory::Resource
            );

            // 初始化响应构建器
            let response_builder = Response::builder()
                // 允许跨域访问 (对于自定义协议很重要)
                .header(ACCESS_CONTROL_ALLOW_ORIGIN, "*");

            match resource_path_result {
                Ok(file_path) => {
                    // 尝试读取文件
                    match fs::read(&file_path) {
                        Ok(data) => {
                            // 5. 自动判断 MIME 类型 (例如 text/html, image/png)
                            let mime_type = mime_guess::from_path(&file_path)
                                .first_or_octet_stream()
                                .as_ref()
                                .to_string();

                            response_builder
                                .header(CONTENT_TYPE, mime_type)
                                .status(StatusCode::OK)
                                .body(data)
                                .unwrap()
                        }
                        Err(_) => {
                            // 文件无法读取或不存在
                            eprintln!("Failed to read file: {:?}", file_path);
                            response_builder
                                .status(StatusCode::NOT_FOUND)
                                .body("File not found".as_bytes().to_vec())
                                .unwrap()
                        }
                    }
                }
                Err(e) => {
                    // 路径解析失败 (比如配置错误)
                    eprintln!("Path resolution failed: {}", e);
                    response_builder
                        .status(StatusCode::INTERNAL_SERVER_ERROR)
                        .body("Internal Error".as_bytes().to_vec())
                        .unwrap()
                }
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}