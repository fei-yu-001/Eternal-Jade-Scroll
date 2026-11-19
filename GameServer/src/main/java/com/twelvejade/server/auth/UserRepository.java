package com.twelvejade.server.auth;

import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;

/** users 表的 Spring Data 仓储。查询都走派生方法，不写 SQL。 */
public interface UserRepository extends JpaRepository<UserEntity, Long> {

    Optional<UserEntity> findByUsername(String username);

    Optional<UserEntity> findByToken(String token);
}
