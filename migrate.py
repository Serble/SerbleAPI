import mysql.connector
from mysql.connector import Error

# ----- CONFIG -----
MYSQL_HOST = 'mysql.example.com'
MYSQL_USER = 'admin'
MYSQL_PASSWORD = 'PASSWORD'
DATABASE_A = 'serble-old'    # Replace with your old database name
DATABASE_B = 'serble-new'    # Replace with your new database name

def migrate():
    try:
        cnx = mysql.connector.connect(
            host=MYSQL_HOST,
            user=MYSQL_USER,
            password=MYSQL_PASSWORD,
        )
        # Connection will automatically disallow modifications if you use correct queries.

        cursor_a = cnx.cursor(dictionary=True)
        cursor_b = cnx.cursor()
        
        # --- KV STORE ---
        cursor_a.execute(f"SELECT k AS `Key`, v AS Value FROM {DATABASE_A}.serblesite_kv")
        kvs = cursor_a.fetchall()
        for row in kvs:
            cursor_b.execute(
                f"INSERT INTO {DATABASE_B}.Kvs (`Key`, Value) VALUES (%s, %s)",
                (row['Key'], row['Value'])
            )
        print("Kvs migrated.")

        # --- Users ---
        cursor_a.execute(f"""
            SELECT
                id AS Id,
                username AS Username,
                email AS Email,
                password AS Password,
                permlevel AS PermLevel,
                verifiedEmail AS VerifiedEmail,
                premiumLevel AS PremiumLevel,
                subscriptionId AS SubscriptionId,
                language AS Language,
                totp_enabled AS TotpEnabled,
                totp_secret AS TotpSecret,
                password_salt AS PasswordSalt
            FROM {DATABASE_A}.serblesite_users
        """)
        users = cursor_a.fetchall()
        for user in users:
            # Patch for nulls/defaults
            for field in ('Username','PermLevel','VerifiedEmail','PremiumLevel','TotpEnabled'):
                if user[field] is None:
                    user[field] = 0 if field in ('PermLevel','VerifiedEmail','PremiumLevel','TotpEnabled') else ''
            cursor_b.execute(
                f"""INSERT INTO {DATABASE_B}.Users (
                        Id, Username, Email, Password, PermLevel, VerifiedEmail,
                        PremiumLevel, SubscriptionId, Language, TotpEnabled, TotpSecret, PasswordSalt
                    ) VALUES (%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s,%s)""",
                (
                    user['Id'], user['Username'], user['Email'], user['Password'], user['PermLevel'],
                    user['VerifiedEmail'], user['PremiumLevel'], user['SubscriptionId'], user['Language'],
                    user['TotpEnabled'], user['TotpSecret'], user['PasswordSalt']
                )
            )
        print("Users migrated.")

        # --- Apps ---
        cursor_a.execute(f"""
            SELECT
                id AS Id,
                ownerid AS OwnerId,
                name AS Name,
                description AS Description,
                clientsecret AS ClientSecret,
                redirecturi AS RedirectUri
            FROM {DATABASE_A}.serblesite_apps
        """)
        apps = cursor_a.fetchall()
        for app in apps:
            for col in ('Id','OwnerId','Name','Description','ClientSecret','RedirectUri'):
                if app[col] is None:
                    app[col] = ''
            cursor_b.execute(
                f"""INSERT INTO {DATABASE_B}.Apps
                    (Id, OwnerId, Name, Description, ClientSecret, RedirectUri)
                    VALUES (%s,%s,%s,%s,%s,%s)""",
                (app['Id'], app['OwnerId'], app['Name'], app['Description'], app['ClientSecret'], app['RedirectUri'])
            )
        print("Apps migrated.")

        # --- Owned Products ---
        cursor_a.execute(f"SELECT user as User, product as Product FROM {DATABASE_A}.serblesite_owned_products")
        owned = cursor_a.fetchall()
        for row in owned:
            if row['User'] is None or row['Product'] is None:
                continue
            cursor_b.execute(
                f"INSERT INTO {DATABASE_B}.OwnedProducts (User, Product) VALUES (%s, %s)",
                (row['User'], row['Product'])
            )
        print("OwnedProducts migrated.")

        # --- User Authorized Apps ---
        cursor_a.execute(f"""
            SELECT userid AS UserId, appid AS AppId, scopes AS Scopes
            FROM {DATABASE_A}.serblesite_user_authorized_apps
        """)
        authorized = cursor_a.fetchall()
        # Build set of valid App Ids and User Ids
        cursor_b.execute(f"SELECT Id FROM {DATABASE_B}.Apps")
        valid_app_ids = {row[0] for row in cursor_b.fetchall()}
        cursor_b.execute(f"SELECT Id FROM {DATABASE_B}.Users")
        valid_user_ids = {row[0] for row in cursor_b.fetchall()}
        
        skipped = 0
        for row in authorized:
            if not row['UserId'] or not row['AppId'] or not row['Scopes']:
                continue
            if row['AppId'] not in valid_app_ids or row['UserId'] not in valid_user_ids:
                skipped += 1
                continue  # skip orphaned references
            cursor_b.execute(
                f"INSERT INTO {DATABASE_B}.UserAuthorizedApps (UserId, AppId, Scopes) VALUES (%s,%s,%s)",
                (row['UserId'], row['AppId'], row['Scopes'])
            )
        print(f"UserAuthorizedApps migrated. Skipped {skipped} invalid rows.")

        # --- User Notes ---
        cursor_a.execute(f"""
            SELECT noteid AS NoteId, user AS User, note AS Note
            FROM {DATABASE_A}.serblesite_user_notes
        """)
        notes = cursor_a.fetchall()
        for note in notes:
            if note['User'] is None or note['NoteId'] is None or note['Note'] is None:
                continue
            cursor_b.execute(
                f"INSERT INTO {DATABASE_B}.UserNotes (NoteId, User, Note) VALUES (%s,%s,%s)",
                (note['NoteId'], note['User'], note['Note'])
            )
        print("UserNotes migrated.")

        cnx.commit()
        print("Migration completed successfully.")

    except Error as e:
        print("Error: ", e)
    finally:
        if 'cursor_a' in locals(): cursor_a.close()
        if 'cursor_b' in locals(): cursor_b.close()
        if 'cnx' in locals(): cnx.close()

if __name__ == "__main__":
    migrate()