import sqlite3
import json
import hashlib
import logging
import os
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional, Any, Dict, List, Tuple
from dataclasses import asdict
import time


class ICache:
    async def get_async(self, key: str) -> Optional[str]:
        raise NotImplementedError

    async def set_async(self, key: str, value: str, expires_at: Optional[datetime] = None) -> None:
        raise NotImplementedError

    async def remove_async(self, key: str) -> None:
        raise NotImplementedError

    async def exists_async(self, key: str) -> bool:
        raise NotImplementedError

    async def clear_async(self) -> None:
        raise NotImplementedError

    async def get_size_async(self) -> int:
        raise NotImplementedError

    async def compact_async(self) -> None:
        raise NotImplementedError

    async def invalidate_pattern_async(self, pattern: str) -> None:
        raise NotImplementedError

    async def store_prompt_async(self, name: str, content: str) -> str:
        raise NotImplementedError

    async def get_prompt_async(self, name: str) -> Optional[Tuple[str, str]]:
        raise NotImplementedError

    async def get_all_entries_async(self) -> List[Dict[str, Any]]:
        raise NotImplementedError


class Cache(ICache):
    def __init__(self, configuration: Dict[str, Any], logger: logging.Logger):
        self.logger = logger
        
        # Simple cache directory - works on all platforms
        cache_dir = configuration.get("cache", {}).get("directory") or os.path.join(tempfile.gettempdir(), "Thaum")
        
        self.logger.debug(f"Creating cache directory: {cache_dir}")
        os.makedirs(cache_dir, exist_ok=True)
        
        db_path = os.path.join(cache_dir, "cache.db")
        self.connection = sqlite3.connect(db_path, check_same_thread=False)
        self.connection.row_factory = sqlite3.Row  # Enable column access by name
        
        self._initialize_database()

    def _initialize_database(self):
        # First, create the basic cache_entries table if it doesn't exist
        CREATE_BASIC_TABLE_SQL = """
        CREATE TABLE IF NOT EXISTS cache_entries (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            type_name TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            expires_at INTEGER,
            last_accessed INTEGER NOT NULL
        );
        """
        
        cursor = self.connection.cursor()
        cursor.execute(CREATE_BASIC_TABLE_SQL)
        
        # Check if new columns exist, and add them if they don't
        check_columns_sql = "PRAGMA table_info(cache_entries)"
        cursor.execute(check_columns_sql)
        columns = cursor.fetchall()
        
        existing_columns = {col[1] for col in columns}  # column name is at index 1
        
        has_prompt_name = "prompt_name" in existing_columns
        has_prompt_hash = "prompt_hash" in existing_columns
        has_model_name = "model_name" in existing_columns
        has_provider_name = "provider_name" in existing_columns
        
        # Add missing columns
        if not has_prompt_name:
            cursor.execute("ALTER TABLE cache_entries ADD COLUMN prompt_name TEXT")
        
        if not has_prompt_hash:
            cursor.execute("ALTER TABLE cache_entries ADD COLUMN prompt_hash TEXT")
        
        if not has_model_name:
            cursor.execute("ALTER TABLE cache_entries ADD COLUMN model_name TEXT")
        
        if not has_provider_name:
            cursor.execute("ALTER TABLE cache_entries ADD COLUMN provider_name TEXT")
        
        # Create prompts table
        create_prompts_table_sql = """
        CREATE TABLE IF NOT EXISTS prompts (
            hash TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            content TEXT NOT NULL,
            created_at INTEGER NOT NULL
        );
        """
        cursor.execute(create_prompts_table_sql)
        
        # Create indexes (one at a time)
        indexes = [
            "CREATE INDEX IF NOT EXISTS idx_expires_at ON cache_entries(expires_at);",
            "CREATE INDEX IF NOT EXISTS idx_last_accessed ON cache_entries(last_accessed);",
            "CREATE INDEX IF NOT EXISTS idx_key_pattern ON cache_entries(key);",
            "CREATE INDEX IF NOT EXISTS idx_prompt_name ON cache_entries(prompt_name);",
            "CREATE INDEX IF NOT EXISTS idx_prompt_hash ON cache_entries(prompt_hash);",
            "CREATE INDEX IF NOT EXISTS idx_model_name ON cache_entries(model_name);",
            "CREATE INDEX IF NOT EXISTS idx_prompt_name_lookup ON prompts(name);"
        ]
        
        for index_sql in indexes:
            cursor.execute(index_sql)
        
        self.connection.commit()
        cursor.close()
        
        self.logger.debug("Cache database initialized with prompt and model tracking")

    def _get_current_timestamp(self) -> int:
        return int(time.time())

    async def get_async(self, key: str) -> Optional[str]:
        try:
            cursor = self.connection.cursor()
            
            # Check if the entry exists and hasn't expired
            sql = """
            SELECT value, expires_at, type_name 
            FROM cache_entries 
            WHERE key = ?
            """
            cursor.execute(sql, (key,))
            result = cursor.fetchone()
            
            if result is None:
                cursor.close()
                return None
            
            value, expires_at, type_name = result
            current_time = self._get_current_timestamp()
            
            # Check if expired
            if expires_at is not None and expires_at <= current_time:
                # Remove expired entry
                cursor.execute("DELETE FROM cache_entries WHERE key = ?", (key,))
                self.connection.commit()
                cursor.close()
                return None
            
            # Update last accessed time
            cursor.execute("UPDATE cache_entries SET last_accessed = ? WHERE key = ?", (current_time, key))
            self.connection.commit()
            cursor.close()
            
            return value
            
        except Exception as ex:
            self.logger.error(f"Failed to get cache entry for key '{key}': {ex}")
            return None

    async def try_get_async(self, key: str) -> Optional[str]:
        return await self.get_async(key)

    async def set_async(self, key: str, value: str, expires_at: Optional[datetime] = None, 
                       prompt_name: Optional[str] = None, prompt_hash: Optional[str] = None, 
                       model_name: Optional[str] = None, provider_name: Optional[str] = None) -> None:
        try:
            cursor = self.connection.cursor()
            
            current_time = self._get_current_timestamp()
            expires_timestamp = int(expires_at.timestamp()) if expires_at else None
            
            # Type inference for simple types
            if isinstance(value, str):
                type_name = "System.String"
            elif isinstance(value, (int, float)):
                type_name = "System.Numeric"
            elif isinstance(value, bool):
                type_name = "System.Boolean"
            elif isinstance(value, (dict, list)):
                type_name = "System.Text.Json.JsonElement"
                value = json.dumps(value)
            else:
                type_name = type(value).__name__
                if hasattr(value, '__dict__'):
                    value = json.dumps(asdict(value))
                else:
                    value = str(value)
            
            sql = """
            INSERT OR REPLACE INTO cache_entries 
            (key, value, type_name, created_at, expires_at, last_accessed, prompt_name, prompt_hash, model_name, provider_name)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """
            cursor.execute(sql, (key, value, type_name, current_time, expires_timestamp, current_time, 
                                prompt_name, prompt_hash, model_name, provider_name))
            
            self.connection.commit()
            cursor.close()
            
        except Exception as ex:
            self.logger.error(f"Failed to set cache entry for key '{key}': {ex}")
            raise

    async def remove_async(self, key: str) -> None:
        try:
            cursor = self.connection.cursor()
            cursor.execute("DELETE FROM cache_entries WHERE key = ?", (key,))
            self.connection.commit()
            cursor.close()
        except Exception as ex:
            self.logger.error(f"Failed to remove cache entry for key '{key}': {ex}")

    async def invalidate_pattern_async(self, pattern: str) -> None:
        try:
            cursor = self.connection.cursor()
            # Simple pattern matching with LIKE
            sql_pattern = pattern.replace('*', '%')
            cursor.execute("DELETE FROM cache_entries WHERE key LIKE ?", (sql_pattern,))
            deleted_count = cursor.rowcount
            self.connection.commit()
            cursor.close()
            
            self.logger.info(f"Invalidated {deleted_count} cache entries matching pattern '{pattern}'")
        except Exception as ex:
            self.logger.error(f"Failed to invalidate cache entries for pattern '{pattern}': {ex}")

    async def clear_async(self) -> None:
        try:
            cursor = self.connection.cursor()
            cursor.execute("DELETE FROM cache_entries")
            cursor.execute("DELETE FROM prompts")
            self.connection.commit()
            cursor.close()
            
            self.logger.info("Cache cleared")
        except Exception as ex:
            self.logger.error(f"Failed to clear cache: {ex}")

    async def exists_async(self, key: str) -> bool:
        try:
            cursor = self.connection.cursor()
            
            sql = """
            SELECT expires_at 
            FROM cache_entries 
            WHERE key = ?
            """
            cursor.execute(sql, (key,))
            result = cursor.fetchone()
            cursor.close()
            
            if result is None:
                return False
            
            expires_at = result[0]
            if expires_at is not None:
                current_time = self._get_current_timestamp()
                if expires_at <= current_time:
                    # Entry expired, remove it
                    await self.remove_async(key)
                    return False
            
            return True
            
        except Exception as ex:
            self.logger.error(f"Failed to check cache entry existence for key '{key}': {ex}")
            return False

    async def get_size_async(self) -> int:
        try:
            cursor = self.connection.cursor()
            cursor.execute("SELECT COUNT(*) FROM cache_entries")
            count = cursor.fetchone()[0]
            cursor.close()
            return count
        except Exception as ex:
            self.logger.error(f"Failed to get cache size: {ex}")
            return 0

    async def compact_async(self) -> None:
        try:
            cursor = self.connection.cursor()
            
            # Remove expired entries
            current_time = self._get_current_timestamp()
            cursor.execute("DELETE FROM cache_entries WHERE expires_at IS NOT NULL AND expires_at <= ?", (current_time,))
            expired_count = cursor.rowcount
            
            # Run VACUUM to reclaim space
            cursor.execute("VACUUM")
            
            self.connection.commit()
            cursor.close()
            
            self.logger.info(f"Cache compacted: removed {expired_count} expired entries")
        except Exception as ex:
            self.logger.error(f"Failed to compact cache: {ex}")

    def generate_prompt_hash(self, content: str) -> str:
        return hashlib.sha256(content.encode()).hexdigest()

    async def store_prompt_async(self, name: str, content: str) -> str:
        try:
            prompt_hash = self.generate_prompt_hash(content)
            current_time = self._get_current_timestamp()
            
            cursor = self.connection.cursor()
            cursor.execute("""
                INSERT OR REPLACE INTO prompts (hash, name, content, created_at)
                VALUES (?, ?, ?, ?)
            """, (prompt_hash, name, content, current_time))
            
            self.connection.commit()
            cursor.close()
            
            return prompt_hash
        except Exception as ex:
            self.logger.error(f"Failed to store prompt '{name}': {ex}")
            raise

    async def get_prompt_async(self, name: str) -> Optional[Tuple[str, str]]:
        try:
            cursor = self.connection.cursor()
            cursor.execute("SELECT hash, content FROM prompts WHERE name = ? ORDER BY created_at DESC LIMIT 1", (name,))
            result = cursor.fetchone()
            cursor.close()
            
            if result:
                return result[0], result[1]  # hash, content
            return None
        except Exception as ex:
            self.logger.error(f"Failed to get prompt '{name}': {ex}")
            return None

    async def get_all_entries_async(self) -> List[Dict[str, Any]]:
        try:
            cursor = self.connection.cursor()
            cursor.execute("""
                SELECT key, type_name, created_at, expires_at, last_accessed, 
                       prompt_name, prompt_hash, model_name, provider_name
                FROM cache_entries
                ORDER BY last_accessed DESC
            """)
            
            results = cursor.fetchall()
            cursor.close()
            
            entries = []
            for row in results:
                entry = {
                    "key": row[0],
                    "type_name": row[1],
                    "created_at": datetime.fromtimestamp(row[2], tz=timezone.utc) if row[2] else None,
                    "expires_at": datetime.fromtimestamp(row[3], tz=timezone.utc) if row[3] else None,
                    "last_accessed": datetime.fromtimestamp(row[4], tz=timezone.utc) if row[4] else None,
                    "prompt_name": row[5],
                    "prompt_hash": row[6],
                    "model_name": row[7],
                    "provider_name": row[8]
                }
                entries.append(entry)
            
            return entries
        except Exception as ex:
            self.logger.error(f"Failed to get all cache entries: {ex}")
            return []

    async def update_last_accessed_async(self, key: str) -> None:
        try:
            cursor = self.connection.cursor()
            current_time = self._get_current_timestamp()
            cursor.execute("UPDATE cache_entries SET last_accessed = ? WHERE key = ?", (current_time, key))
            self.connection.commit()
            cursor.close()
        except Exception as ex:
            self.logger.error(f"Failed to update last accessed for key '{key}': {ex}")

    def dispose(self):
        if self.connection:
            self.connection.close()

    def __del__(self):
        self.dispose()


class MockCache(ICache):
    def __init__(self, logger: logging.Logger):
        self.logger = logger
        self.logger.warning("MockCache is a no-op implementation")

    async def get_async(self, key: str) -> Optional[str]:
        return None

    async def set_async(self, key: str, value: str, expires_at: Optional[datetime] = None) -> None:
        pass

    async def remove_async(self, key: str) -> None:
        pass

    async def exists_async(self, key: str) -> bool:
        return False

    async def clear_async(self) -> None:
        pass

    async def get_size_async(self) -> int:
        return 0

    async def compact_async(self) -> None:
        pass

    async def invalidate_pattern_async(self, pattern: str) -> None:
        pass

    async def store_prompt_async(self, name: str, content: str) -> str:
        import hashlib
        return hashlib.sha256(content.encode()).hexdigest()

    async def get_prompt_async(self, name: str) -> Optional[Tuple[str, str]]:
        return None

    async def get_all_entries_async(self) -> List[Dict[str, Any]]:
        return []