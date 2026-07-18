CREATE TRIGGER TR_fn_identity_user_UuidBinary_BU
BEFORE UPDATE ON fn_identity_user
FOR EACH ROW
SET NEW.IdBinary = UUID_TO_BIN(NEW.Id, 0);
