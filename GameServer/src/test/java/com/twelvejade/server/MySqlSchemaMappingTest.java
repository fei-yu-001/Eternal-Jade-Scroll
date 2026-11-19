package com.twelvejade.server;

import com.twelvejade.server.auth.UserEntity;
import com.twelvejade.server.save.SaveEntity;
import org.hibernate.boot.MetadataSources;
import org.hibernate.boot.registry.StandardServiceRegistryBuilder;
import org.hibernate.mapping.Column;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;

class MySqlSchemaMappingTest {
    @Test
    @DisplayName("MySQL 存档列匹配版本化 DDL 的 LONGTEXT，可容纳 256KB 存档")
    void savePayloadUsesLongtextInsteadOfTinytext() {
        var registry = new StandardServiceRegistryBuilder()
                .applySetting("hibernate.boot.allow_jdbc_metadata_access", false)
                .applySetting("hibernate.dialect", "org.hibernate.dialect.MySQLDialect")
                .build();
        try {
            var metadata = new MetadataSources(registry)
                    .addAnnotatedClass(UserEntity.class)
                    .addAnnotatedClass(SaveEntity.class)
                    .buildMetadata();
            var payload = metadata.getEntityBinding(SaveEntity.class.getName())
                    .getTable().getColumn(new Column("payload"));
            assertEquals("longtext", payload.getSqlType(metadata));
        } finally {
            StandardServiceRegistryBuilder.destroy(registry);
        }
    }
}
