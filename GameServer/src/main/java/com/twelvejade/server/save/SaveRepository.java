package com.twelvejade.server.save;

import java.util.List;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;

/** saves 表的 Spring Data 仓储。查询都走派生方法，不写 SQL。 */
public interface SaveRepository extends JpaRepository<SaveEntity, Long> {

    /** 取某用户某槽位的存档；没有则 empty（空槽位是正常状态）。 */
    Optional<SaveEntity> findByUser_IdAndSlot(Long userId, byte slot);

    /** 某用户全部存档，按槽位升序——给 /api/save/list 拼固定三槽位回执用。 */
    List<SaveEntity> findAllByUser_IdOrderBySlotAsc(Long userId);
}
