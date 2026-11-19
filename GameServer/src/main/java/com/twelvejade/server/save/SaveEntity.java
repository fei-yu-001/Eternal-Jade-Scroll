package com.twelvejade.server.save;

import com.twelvejade.server.auth.UserEntity;
import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.FetchType;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import jakarta.persistence.UniqueConstraint;
import java.time.Instant;
import org.hibernate.Length;

/**
 * saves 表实体：某用户某个槽位上的一份云存档。表结构与 Database/SQL/V1__user_cloud_save.sql 一一对应。
 *
 * <p>{@code (user_id, slot)} 全库唯一：同一用户同一槽位只有一条记录，
 * 上传走 INSERT-or-UPDATE（覆盖语义），不存在一槽多版本。
 *
 * <p>payload 是客户端存档 JSON 原文，服务端只存不读、不解释——客户端存档的真相在客户端，
 * 服务端不成为第二真相（M6-01 定下的边界，继续遵守）。
 */
@Entity
@Table(name = "saves",
        uniqueConstraints = @UniqueConstraint(name = "uk_saves_user_slot",
                columnNames = {"user_id", "slot"}))
public class SaveEntity {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    /** 存档归属的用户。 */
    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "user_id", nullable = false)
    private UserEntity user;

    /** 槽位 1–3，与客户端三档一致。byte 对应 DDL 的 TINYINT。 */
    @Column(nullable = false)
    private byte slot;

    /** 上传时客户端声明的存档结构版本，服务端原样存档、不参与逻辑。 */
    @Column(name = "schema_version", nullable = false)
    private int schemaVersion;

    /** 普通长文本映射到 MySQL LONGTEXT；不能用 @Lob 的 JDBC CLOB 语义替代。 */
    @Column(name = "payload", nullable = false, length = Length.LONG32)
    private String payload;

    /**
     * 最近一次覆盖上传的时间。{@code Instant} 自描述 UTC 语义（Hibernate 按 UTC 存取），
     * 序列化输出带偏移的 ISO8601（如 2026-10-04T01:30:00.123Z）。
     */
    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    public Long getId() {
        return id;
    }

    public UserEntity getUser() {
        return user;
    }

    public void setUser(UserEntity user) {
        this.user = user;
    }

    public byte getSlot() {
        return slot;
    }

    public void setSlot(byte slot) {
        this.slot = slot;
    }

    public int getSchemaVersion() {
        return schemaVersion;
    }

    public void setSchemaVersion(int schemaVersion) {
        this.schemaVersion = schemaVersion;
    }

    public String getPayload() {
        return payload;
    }

    public void setPayload(String payload) {
        this.payload = payload;
    }

    public Instant getUpdatedAt() {
        return updatedAt;
    }

    public void setUpdatedAt(Instant updatedAt) {
        this.updatedAt = updatedAt;
    }
}
